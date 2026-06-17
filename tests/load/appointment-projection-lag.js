import http from "k6/http";
import { check, sleep } from "k6";
import { Trend, Counter, Rate } from "k6/metrics";
import encoding from "k6/encoding";

const appointmentProjectionCreateLagMs = new Trend("appointment_projection_create_lag_ms", true);
const appointmentProjectionRescheduleLagMs = new Trend("appointment_projection_reschedule_lag_ms", true);
const appointmentProjectionCancelLagMs = new Trend("appointment_projection_cancel_lag_ms", true);
const appointmentProjectionTimeoutRate = new Rate("appointment_projection_timeout_rate");
const appointmentProjectionWrongStateRate = new Rate("appointment_projection_wrong_state_rate");
const appointmentProjectionPollRequests = new Counter("appointment_projection_poll_requests");

const CLINIC_CLAIM = "clinic_id";
const LIST_PAGE_SIZE = 5;
const SLOT_INTERVAL_MINUTES = 15;
const DEFAULT_DURATION_MINUTES = 30;
const ISTANBUL_OFFSET_MINUTES = 180;
const WORK_START_LOCAL_MINUTES = 9 * 60;
const WORK_END_LOCAL_MINUTES = 18 * 60;
const BASE_DAY_OFFSET = 120;
const MAX_FUTURE_YEARS = 2;
const LOAD_TEST_NOTE_PREFIX = "K6_LOAD_TEST";
const RESCHEDULE_PHASE_OFFSET = 2;
const APPOINTMENT_TYPE_EXAMINATION = 0;
const APPOINTMENT_STATUS_CANCELLED = 2;

const POLL_INTERVAL_MS = Number.parseInt(__ENV.PROJECTION_POLL_INTERVAL_MS || "200", 10);
const PROJECTION_TIMEOUT_MS = Number.parseInt(__ENV.PROJECTION_TIMEOUT_MS || "10000", 10);

let lifecycleSequence = 0;

function resolveBaseUrl() {
    const rawUrl = __ENV.VETINITY_URL;
    if (!rawUrl || String(rawUrl).trim() === "") {
        throw new Error("VETINITY_URL ortam degiskeni zorunludur.");
    }
    return String(rawUrl).replace(/\/$/, "");
}

function readTokenSourceJson() {
    const tokensFile = String(__ENV.VETINITY_TOKENS_FILE || "").trim();
    if (tokensFile) {
        try {
            return { raw: open(tokensFile), sourceLabel: "VETINITY_TOKENS_FILE" };
        } catch (_error) {
            throw new Error(`VETINITY_TOKENS_FILE okunamadi: ${tokensFile}`);
        }
    }

    throw new Error("VETINITY_TOKENS_FILE zorunludur (projection lag testi).");
}

function decodeJwtPayload(accessToken) {
    const parts = String(accessToken).split(".");
    if (parts.length < 2 || !parts[1]) {
        throw new Error("JWT payload cozulemedi.");
    }

    try {
        const json = encoding.b64decode(parts[1], "rawurl", "s");
        return JSON.parse(json);
    } catch (_error) {
        throw new Error("JWT payload cozulemedi.");
    }
}

function normalizeSessions(raw, sourceLabel) {
    let parsed;
    try {
        parsed = JSON.parse(raw);
    } catch (_error) {
        throw new Error(`${sourceLabel}: JSON parse edilemedi.`);
    }

    if (!Array.isArray(parsed) || parsed.length === 0) {
        throw new Error(`${sourceLabel}: token dizisi bos veya gecersiz.`);
    }

    const sessions = [];
    for (let index = 0; index < parsed.length; index++) {
        const entry = parsed[index];
        const slot = entry && entry.slot !== undefined ? String(entry.slot).trim() : "";
        const accessToken =
            entry && entry.accessToken !== undefined ? String(entry.accessToken).trim() : "";

        if (!slot || !accessToken) {
            throw new Error(`${sourceLabel}: slot/accessToken eksik (index ${index}).`);
        }

        const payload = decodeJwtPayload(accessToken);
        const clinicId = payload[CLINIC_CLAIM];
        if (!clinicId) {
            throw new Error(`${sourceLabel}: slot ${slot} icin clinic_id claim eksik.`);
        }

        sessions.push({ slot, accessToken, clinicId: String(clinicId).trim() });
    }

    return sessions;
}

function buildRuntimeConfig() {
    const baseUrl = resolveBaseUrl();
    const { raw, sourceLabel } = readTokenSourceJson();
    const sessions = normalizeSessions(raw, sourceLabel);
    const vus = Number.parseInt(__ENV.VUS || __ENV.PROJECTION_LAG_VUS || "2", 10);

    if (!Number.isFinite(vus) || vus < 1) {
        throw new Error("VUS gecerli bir pozitif tamsayi olmalidir.");
    }

    return { baseUrl, sessions, vus };
}

const CONFIG = buildRuntimeConfig();
const SESSIONS = CONFIG.sessions;

const duration = __ENV.DURATION || __ENV.PROJECTION_LAG_DURATION || "5m";

export const options = {
    insecureSkipTLSVerify: true,
    vus: CONFIG.vus,
    duration,
    thresholds: {
        http_req_failed: ["rate<0.01"],
        appointment_projection_timeout_rate: ["rate==0"],
        appointment_projection_wrong_state_rate: ["rate==0"],
        appointment_projection_create_lag_ms: ["p(95)<2000", "p(99)<5000"],
        appointment_projection_reschedule_lag_ms: ["p(95)<2000", "p(99)<5000"],
        appointment_projection_cancel_lag_ms: ["p(95)<2000", "p(99)<5000"],
    },
};

function resolveSessionForVu() {
    return SESSIONS[(__VU - 1) % SESSIONS.length];
}

function buildHeaders(accessToken, includeJson = false) {
    const headers = {
        Authorization: `Bearer ${accessToken}`,
        Accept: "application/json",
    };
    if (includeJson) {
        headers["Content-Type"] = "application/json";
    }
    return headers;
}

function buildQueryString(params) {
    return Object.entries(params)
        .filter(([, value]) => value !== undefined && value !== null)
        .map(
            ([key, value]) =>
                `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`
        )
        .join("&");
}

function buildAppointmentListUrl(baseUrl, petId, dateFromUtc, dateToUtc) {
    const query = buildQueryString({
        page: 1,
        pageSize: LIST_PAGE_SIZE,
        petId,
        dateFromUtc,
        dateToUtc,
    });
    return `${baseUrl}/api/v1/appointments?${query}`;
}

function buildAppointmentCreateUrl(baseUrl) {
    return `${baseUrl}/api/v1/appointments`;
}

function buildAppointmentRescheduleUrl(baseUrl, appointmentId) {
    return `${baseUrl}/api/v1/appointments/${appointmentId}/reschedule`;
}

function buildAppointmentCancelUrl(baseUrl, appointmentId) {
    return `${baseUrl}/api/v1/appointments/${appointmentId}/cancel`;
}

function buildPetListUrl(baseUrl) {
    const query = buildQueryString({ page: 1, pageSize: 20 });
    return `${baseUrl}/api/v1/pets?${query}`;
}

function getIstanbulDayOfWeek(utcDate) {
    const shifted = new Date(utcDate.getTime() + ISTANBUL_OFFSET_MINUTES * 60000);
    return shifted.getUTCDay();
}

function getWeekdaySlotCapacity(dayOfWeek) {
    if (dayOfWeek === 0) {
        return 0;
    }

    const workEndLocalMinutes = dayOfWeek === 6 ? 14 * 60 : WORK_END_LOCAL_MINUTES;
    const lastStartLocalMinutes = workEndLocalMinutes - DEFAULT_DURATION_MINUTES;

    if (lastStartLocalMinutes < WORK_START_LOCAL_MINUTES) {
        return 0;
    }

    return (
        Math.floor(
            (lastStartLocalMinutes - WORK_START_LOCAL_MINUTES) / SLOT_INTERVAL_MINUTES
        ) + 1
    );
}

function istanbulLocalDateTimeToUtc(dayCursorUtcMidnight, localMinutes) {
    const localHour = Math.floor(localMinutes / 60);
    const localMinute = localMinutes % 60;
    const utcHour = localHour - 3;
    const scheduled = new Date(dayCursorUtcMidnight);

    if (utcHour < 0) {
        scheduled.setUTCDate(scheduled.getUTCDate() - 1);
        scheduled.setUTCHours(utcHour + 24, localMinute, 0, 0);
    } else {
        scheduled.setUTCHours(utcHour, localMinute, 0, 0);
    }

    return scheduled;
}

function buildScheduledAtUtcFromLinearSlot(linearSlot) {
    let remaining = linearSlot;
    let dayOffset = BASE_DAY_OFFSET + __VU;
    const now = Date.now();
    const maxFutureMs = now + MAX_FUTURE_YEARS * 365 * 24 * 60 * 60 * 1000;

    for (let guard = 0; guard < 5000; guard++) {
        const cursor = new Date();
        cursor.setUTCHours(0, 0, 0, 0);
        cursor.setUTCDate(cursor.getUTCDate() + dayOffset);

        const dayOfWeek = getIstanbulDayOfWeek(cursor);
        const capacity = getWeekdaySlotCapacity(dayOfWeek);

        if (capacity === 0) {
            dayOffset += 1;
            continue;
        }

        if (remaining < capacity) {
            const localMinutes =
                WORK_START_LOCAL_MINUTES + remaining * SLOT_INTERVAL_MINUTES;
            const scheduled = istanbulLocalDateTimeToUtc(cursor, localMinutes);

            if (scheduled.getTime() > now && scheduled.getTime() <= maxFutureMs) {
                return scheduled.toISOString();
            }

            return null;
        }

        remaining -= capacity;
        dayOffset += 1;
    }

    return null;
}

function buildLifecycleSchedule(seq) {
    const createLinearSlot = seq * 10 + __VU;
    const rescheduleLinearSlot = createLinearSlot + RESCHEDULE_PHASE_OFFSET;

    const createScheduledAtUtc = buildScheduledAtUtcFromLinearSlot(createLinearSlot);
    const rescheduleScheduledAtUtc =
        buildScheduledAtUtcFromLinearSlot(rescheduleLinearSlot);

    if (!createScheduledAtUtc || !rescheduleScheduledAtUtc) {
        return null;
    }

    return { createScheduledAtUtc, rescheduleScheduledAtUtc };
}

function buildNarrowDateRange(scheduledAtUtc) {
    const scheduled = new Date(scheduledAtUtc);
    const from = new Date(scheduled);
    from.setUTCDate(from.getUTCDate() - 1);
    from.setUTCHours(0, 0, 0, 0);

    const to = new Date(scheduled);
    to.setUTCDate(to.getUTCDate() + 1);
    to.setUTCHours(23, 59, 59, 999);

    return {
        dateFromUtc: from.toISOString(),
        dateToUtc: to.toISOString(),
    };
}

function normalizeGuid(value) {
    return String(value || "").trim().toLowerCase();
}

function normalizeScheduledAtUtc(value) {
    if (!value) {
        return null;
    }
    const parsed = new Date(value);
    if (Number.isNaN(parsed.getTime())) {
        return null;
    }
    return parsed.getTime();
}

function scheduledAtMatches(itemValue, expectedIso) {
    const actualMs = normalizeScheduledAtUtc(itemValue);
    const expectedMs = normalizeScheduledAtUtc(expectedIso);
    if (actualMs === null || expectedMs === null) {
        return false;
    }
    return Math.abs(actualMs - expectedMs) < 1000;
}

function parseCreatedAppointmentId(response) {
    if (response.status !== 201) {
        return null;
    }

    const rawBody = String(response.body || "").trim();
    if (!rawBody) {
        return null;
    }

    try {
        const parsed = JSON.parse(rawBody);
        if (typeof parsed === "string") {
            return parsed;
        }
        if (parsed && parsed.id) {
            return String(parsed.id);
        }
    } catch (_error) {
        return rawBody.replace(/^"|"$/g, "");
    }

    return null;
}

function fetchAppointmentFromQueryList(session, petId, dateRange, appointmentId) {
    const url = buildAppointmentListUrl(
        CONFIG.baseUrl,
        petId,
        dateRange.dateFromUtc,
        dateRange.dateToUtc
    );

    appointmentProjectionPollRequests.add(1);

    const response = http.get(url, {
        headers: buildHeaders(session.accessToken),
        tags: {
            endpoint: "appointment_list",
            phase: "projection_poll",
            clinic_slot: session.slot,
        },
    });

    if (response.status !== 200) {
        return { found: false, item: null, httpStatus: response.status };
    }

    let body;
    try {
        body = response.json();
    } catch (_error) {
        return { found: false, item: null, httpStatus: response.status };
    }

    if (!body || !Array.isArray(body.items)) {
        return { found: false, item: null, httpStatus: response.status };
    }

    const targetId = normalizeGuid(appointmentId);
    const item = body.items.find((row) => normalizeGuid(row.id) === targetId);
    return { found: !!item, item: item || null, httpStatus: response.status };
}

function pollUntilPredicate(session, petId, dateRange, appointmentId, predicate, wrongStateCheck) {
    const started = Date.now();
    const deadline = started + PROJECTION_TIMEOUT_MS;

    while (Date.now() < deadline) {
        const result = fetchAppointmentFromQueryList(
            session,
            petId,
            dateRange,
            appointmentId
        );

        if (result.found && result.item) {
            if (wrongStateCheck && wrongStateCheck(result.item)) {
                appointmentProjectionWrongStateRate.add(1);
                return { ok: false, lagMs: Date.now() - started, timedOut: false, wrongState: true };
            }

            if (predicate(result.item)) {
                appointmentProjectionTimeoutRate.add(0);
                appointmentProjectionWrongStateRate.add(0);
                return { ok: true, lagMs: Date.now() - started, timedOut: false, wrongState: false };
            }
        }

        sleep(POLL_INTERVAL_MS / 1000);
    }

    appointmentProjectionTimeoutRate.add(1);
    return { ok: false, lagMs: PROJECTION_TIMEOUT_MS, timedOut: true, wrongState: false };
}

function resolvePetIdsForSession(session) {
    const response = http.get(buildPetListUrl(CONFIG.baseUrl), {
        headers: buildHeaders(session.accessToken),
        tags: { endpoint: "pet_list", phase: "setup", clinic_slot: session.slot },
    });

    if (response.status !== 200) {
        throw new Error(`Setup slot ${session.slot}: pet listesi alinamadi (HTTP ${response.status}).`);
    }

    const body = response.json();
    if (!body || !Array.isArray(body.items) || body.items.length === 0) {
        throw new Error(`Setup slot ${session.slot}: kullanilabilir pet yok.`);
    }

    const petIds = body.items
        .map((item) => item && item.id)
        .filter((id) => id !== null && id !== undefined && String(id).trim() !== "")
        .map((id) => String(id));

    if (petIds.length === 0) {
        throw new Error(`Setup slot ${session.slot}: pet id bulunamadi.`);
    }

    return petIds;
}

export function setup() {
    const petIdsBySlot = {};
    for (const session of SESSIONS) {
        petIdsBySlot[session.slot] = resolvePetIdsForSession(session);
    }
    return { petIdsBySlot };
}

export default function (data) {
    const session = resolveSessionForVu();
    const petIds = data.petIdsBySlot[session.slot];
    if (!petIds || petIds.length === 0) {
        throw new Error(`Slot ${session.slot}: setup pet listesi bulunamadi.`);
    }
    const petId = petIds[(__VU - 1) % petIds.length];
    const seq = lifecycleSequence;
    lifecycleSequence += 1;

    const schedule = buildLifecycleSchedule(seq);
    if (!schedule) {
        sleep(1);
        return;
    }

    const note = `${LOAD_TEST_NOTE_PREFIX} lag vu=${__VU} seq=${seq} ts=${Date.now()}`;
    const dateRange = buildNarrowDateRange(schedule.createScheduledAtUtc);

    const createResponse = http.post(
        buildAppointmentCreateUrl(CONFIG.baseUrl),
        JSON.stringify({
            petId,
            scheduledAtUtc: schedule.createScheduledAtUtc,
            appointmentType: APPOINTMENT_TYPE_EXAMINATION,
            notes: note,
        }),
        {
            headers: buildHeaders(session.accessToken, true),
            tags: {
                endpoint: "appointment_create",
                phase: "projection_lag",
                clinic_slot: session.slot,
            },
        }
    );

    check(createResponse, {
        "create HTTP 201": (r) => r.status === 201,
    });

    const appointmentId = parseCreatedAppointmentId(createResponse);
    if (!appointmentId) {
        sleep(1);
        return;
    }

    const createPoll = pollUntilPredicate(
        session,
        petId,
        dateRange,
        appointmentId,
        () => true,
        null
    );
    appointmentProjectionCreateLagMs.add(createPoll.lagMs);

    if (!createPoll.ok) {
        return;
    }

    const rescheduleResponse = http.post(
        buildAppointmentRescheduleUrl(CONFIG.baseUrl, appointmentId),
        JSON.stringify({ scheduledAtUtc: schedule.rescheduleScheduledAtUtc }),
        {
            headers: buildHeaders(session.accessToken, true),
            tags: {
                endpoint: "appointment_reschedule",
                phase: "projection_lag",
                clinic_slot: session.slot,
            },
        }
    );

    check(rescheduleResponse, {
        "reschedule HTTP 204": (r) => r.status === 204,
    });

    if (rescheduleResponse.status !== 204) {
        sleep(1);
        return;
    }

    const expectedRescheduleUtc = schedule.rescheduleScheduledAtUtc;
    const reschedulePoll = pollUntilPredicate(
        session,
        petId,
        buildNarrowDateRange(schedule.rescheduleScheduledAtUtc),
        appointmentId,
        (item) => scheduledAtMatches(item.scheduledAtUtc, expectedRescheduleUtc),
        null
    );
    appointmentProjectionRescheduleLagMs.add(reschedulePoll.lagMs);

    if (!reschedulePoll.ok) {
        return;
    }

    const cancelResponse = http.post(
        buildAppointmentCancelUrl(CONFIG.baseUrl, appointmentId),
        JSON.stringify({
            reason: `${LOAD_TEST_NOTE_PREFIX} lag cancel vu=${__VU} seq=${seq}`,
        }),
        {
            headers: buildHeaders(session.accessToken, true),
            tags: {
                endpoint: "appointment_cancel",
                phase: "projection_lag",
                clinic_slot: session.slot,
            },
        }
    );

    check(cancelResponse, {
        "cancel HTTP 204": (r) => r.status === 204,
    });

    if (cancelResponse.status !== 204) {
        sleep(1);
        return;
    }

    const cancelPoll = pollUntilPredicate(
        session,
        petId,
        dateRange,
        appointmentId,
        (item) => Number(item.status) === APPOINTMENT_STATUS_CANCELLED,
        (item) => Number(item.status) === 1
    );
    appointmentProjectionCancelLagMs.add(cancelPoll.lagMs);
    sleep(1);
}
