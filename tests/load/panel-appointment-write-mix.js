import http from "k6/http";
import { check, sleep } from "k6";
import { Trend, Counter, Rate } from "k6/metrics";
import encoding from "k6/encoding";

const dashboardDuration = new Trend("dashboard_duration", true);
const appointmentListDuration = new Trend("appointment_list_duration", true);
const appointmentCalendarDuration = new Trend("appointment_calendar_duration", true);
const clientListDuration = new Trend("client_list_duration", true);
const petListDuration = new Trend("pet_list_duration", true);

const appointmentCreateDuration = new Trend("appointment_create_duration", true);
const appointmentRescheduleDuration = new Trend("appointment_reschedule_duration", true);
const appointmentCancelDuration = new Trend("appointment_cancel_duration", true);

const appointmentWriteFailures = new Counter("appointment_write_failures");
const appointmentCreateSuccess = new Counter("appointment_create_success");
const appointmentCancelSuccess = new Counter("appointment_cancel_success");
const appointmentScheduleGenerationFailures = new Counter(
    "appointment_schedule_generation_failures"
);
const appointmentCleanupFailures = new Counter("appointment_cleanup_failures");

const status401 = new Counter("status_401");
const status403 = new Counter("status_403");
const status429 = new Counter("status_429");
const unexpectedStatus = new Counter("unexpected_status");
const appointmentSlotConflictRate = new Rate("appointment_slot_conflict_rate");
const appointmentValidationFailureRate = new Rate("appointment_validation_failure_rate");
const appointmentAuthFailureRate = new Rate("appointment_auth_failure_rate");
const appointmentServerFailureRate = new Rate("appointment_server_failure_rate");
const appointmentNetworkFailureRate = new Rate("appointment_network_failure_rate");
const endpointCheckFailures = new Counter("endpoint_check_failures");
const clinicSlotRequests = new Counter("clinic_slot_requests");

const CLINIC_CLAIM = "clinic_id";
const LIST_PAGE_SIZE = 20;
const SLOT_INTERVAL_MINUTES = 15;
const DEFAULT_DURATION_MINUTES = 30;
const ISTANBUL_OFFSET_MINUTES = 180;
const WORK_START_LOCAL_MINUTES = 9 * 60;
const WORK_END_LOCAL_MINUTES = 18 * 60;
const BASE_DAY_OFFSET = 120;
const MAX_FUTURE_YEARS = 2;
const LOAD_TEST_NOTE_PREFIX = "K6_LOAD_TEST";
const RESCHEDULE_PHASE_OFFSET =
    Math.ceil(DEFAULT_DURATION_MINUTES / SLOT_INTERVAL_MINUTES) + 1;
const SLOT_RING_PER_VU = 8;
const LIFECYCLE_BLOCK_SLOTS = 4;
const APPOINTMENT_TYPE_EXAMINATION = 0;

let writeSequence = 0;

const READ_SCENARIOS = [
    { key: "dashboard", weight: 25, tag: "dashboard", trend: dashboardDuration },
    { key: "appointment_list", weight: 25, tag: "appointment_list", trend: appointmentListDuration },
    { key: "appointment_calendar", weight: 20, tag: "appointment_calendar", trend: appointmentCalendarDuration },
    { key: "client_list", weight: 15, tag: "client_list", trend: clientListDuration },
    { key: "pet_list", weight: 15, tag: "pet_list", trend: petListDuration },
];

const PREFLIGHT_CHECKS = [
    { endpoint: "dashboard", buildUrl: (baseUrl) => buildDashboardUrl(baseUrl), validate: validateDashboardShape },
    { endpoint: "appointment_list", buildUrl: (baseUrl) => buildAppointmentListUrl(baseUrl, 1), validate: validatePagedShape },
    {
        endpoint: "appointment_calendar",
        buildUrl: (baseUrl, calendarRange) =>
            buildAppointmentCalendarUrl(baseUrl, calendarRange.dateFromUtc, calendarRange.dateToUtc),
        validate: validateCalendarShape,
    },
    { endpoint: "client_list", buildUrl: (baseUrl) => buildClientListUrl(baseUrl, 1), validate: validatePagedShape },
    { endpoint: "pet_list", buildUrl: (baseUrl) => buildPetListUrl(baseUrl, 1), validate: validatePagedShape },
];

function resolveBaseUrl() {
    const rawUrl = __ENV.VETINITY_URL;
    if (!rawUrl || String(rawUrl).trim() === "") {
        throw new Error(
            "VETINITY_URL ortam degiskeni zorunludur. Ornek: VETINITY_URL=https://localhost:7173"
        );
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

    try {
        return {
            raw: open("../.tokens/clinic-tokens.json"),
            sourceLabel: "tests/load/.tokens/clinic-tokens.json",
        };
    } catch (_error) {
        throw new Error(
            "Token kaynagi bulunamadi. VETINITY_TOKENS_FILE tanimlayin veya tests/load/.tokens/clinic-tokens.json olusturun."
        );
    }
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

function extractClinicIdFromPayload(payload, sourceLabel, slot) {
    const clinicId = payload[CLINIC_CLAIM];
    if (clinicId === null || clinicId === undefined || String(clinicId).trim() === "") {
        throw new Error(
            `${sourceLabel}: slot ${slot} icin JWT payload ${CLINIC_CLAIM} claim eksik.`
        );
    }
    return String(clinicId).trim();
}

function normalizeSessions(raw, sourceLabel) {
    let parsed;
    try {
        parsed = JSON.parse(raw);
    } catch (_error) {
        throw new Error(`${sourceLabel}: JSON parse edilemedi.`);
    }

    if (!Array.isArray(parsed)) {
        throw new Error(`${sourceLabel}: JSON bir dizi olmalidir.`);
    }

    if (parsed.length === 0) {
        throw new Error(`${sourceLabel}: token dizisi bos.`);
    }

    const slotsSeen = new Set();
    const tokensSeen = new Set();
    const clinicIdsSeen = new Set();
    const sessions = [];

    for (let index = 0; index < parsed.length; index++) {
        const entry = parsed[index];
        const slot = entry && entry.slot !== undefined ? String(entry.slot).trim() : "";
        const accessToken =
            entry && entry.accessToken !== undefined ? String(entry.accessToken).trim() : "";

        if (!slot) {
            throw new Error(`${sourceLabel}: slot bos (index ${index}).`);
        }
        if (!accessToken) {
            throw new Error(`${sourceLabel}: accessToken bos (slot ${slot}).`);
        }
        if (slotsSeen.has(slot)) {
            throw new Error(`${sourceLabel}: duplicate slot (${slot}).`);
        }
        if (tokensSeen.has(accessToken)) {
            throw new Error(`${sourceLabel}: duplicate accessToken (slot ${slot}).`);
        }

        const payload = decodeJwtPayload(accessToken);
        const clinicId = extractClinicIdFromPayload(payload, sourceLabel, slot);
        if (clinicIdsSeen.has(clinicId)) {
            throw new Error(`${sourceLabel}: duplicate ${CLINIC_CLAIM} (slot ${slot}).`);
        }

        slotsSeen.add(slot);
        tokensSeen.add(accessToken);
        clinicIdsSeen.add(clinicId);
        sessions.push({ slot, accessToken, clinicId });
    }

    return sessions;
}

function loadSessions() {
    const { raw, sourceLabel } = readTokenSourceJson();
    return normalizeSessions(raw, sourceLabel);
}

function resolveWritePercent() {
    const raw = __ENV.WRITE_PERCENT;
    const value = raw === undefined || raw === null || String(raw).trim() === ""
        ? 5
        : Number.parseFloat(String(raw));

    if (!Number.isFinite(value) || value < 0 || value > 100) {
        throw new Error("WRITE_PERCENT 0 ile 100 arasinda olmalidir.");
    }

    return value;
}

function buildRuntimeConfig() {
    const baseUrl = resolveBaseUrl();
    const sessions = loadSessions();
    const vus = Number.parseInt(__ENV.VUS || "1", 10);
    const writePercent = resolveWritePercent();

    if (!Number.isFinite(vus) || vus < 1) {
        throw new Error("VUS gecerli bir pozitif tamsayi olmalidir.");
    }

    if (vus % sessions.length !== 0) {
        throw new Error(
            `VUS (${vus}) token sayisi (${sessions.length}) ile tam bolunmelidir.`
        );
    }

    validateScheduleCapacity(vus);

    return { baseUrl, sessions, vus, writePercent };
}

const CONFIG = buildRuntimeConfig();
const SESSIONS = CONFIG.sessions;
const WRITE_PERCENT = CONFIG.writePercent;

const duration = __ENV.DURATION || "30s";
const thinkTimeMin = Number.parseFloat(__ENV.THINK_TIME_MIN || "0.5");
const thinkTimeMax = Number.parseFloat(__ENV.THINK_TIME_MAX || "1.5");

function buildClinicSlotThresholds(sessions) {
    const thresholds = {};

    for (const session of sessions) {
        thresholds[`clinic_slot_requests{clinic_slot:${session.slot}}`] = ["count>0"];
    }

    return thresholds;
}

export const options = {
    insecureSkipTLSVerify: true,
    vus: CONFIG.vus,
    duration,
    thresholds: {
        http_req_failed: ["rate<0.01"],
        checks: ["rate>0.99"],
        "http_req_duration{phase:load}": ["p(95)<1000", "p(99)<2000"],
        "http_req_duration{endpoint:dashboard,phase:load}": ["p(95)<1000"],
        "http_req_duration{endpoint:appointment_list,phase:load}": ["p(95)<1000"],
        "http_req_duration{endpoint:appointment_calendar,phase:load}": ["p(95)<1000"],
        "http_req_duration{endpoint:client_list,phase:load}": ["p(95)<1000"],
        "http_req_duration{endpoint:pet_list,phase:load}": ["p(95)<1000"],
        dashboard_duration: ["p(95)<1000"],
        appointment_list_duration: ["p(95)<1000"],
        appointment_calendar_duration: ["p(95)<1000"],
        client_list_duration: ["p(95)<1000"],
        pet_list_duration: ["p(95)<1000"],
        appointment_create_duration: ["p(95)<1500"],
        appointment_reschedule_duration: ["p(95)<1500"],
        appointment_cancel_duration: ["p(95)<1500"],
        appointment_schedule_generation_failures: ["count==0"],
        appointment_cleanup_failures: ["count==0"],
        status_401: ["count==0"],
        status_403: ["count==0"],
        status_429: ["count==0"],
        ...buildClinicSlotThresholds(SESSIONS),
    },
};

function resolveSessionForVu() {
    const sessionIndex = (__VU - 1) % SESSIONS.length;
    return SESSIONS[sessionIndex];
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

function trackStatus(status) {
    if (status === 401) {
        status401.add(1);
        return;
    }
    if (status === 403) {
        status403.add(1);
        return;
    }
    if (status === 429) {
        status429.add(1);
        return;
    }
    if (status !== 200 && status !== 201 && status !== 204) {
        unexpectedStatus.add(1);
    }
}

function trackWriteStatus(status, errorCode) {
    if (status === 0) {
        appointmentNetworkFailureRate.add(1);
        return;
    }
    if (status === 401 || status === 403) {
        appointmentAuthFailureRate.add(1);
        trackStatus(status);
        return;
    }
    if (status === 400 || status === 422) {
        appointmentValidationFailureRate.add(1);
        return;
    }
    if (status === 409 || (errorCode && String(errorCode).toLowerCase().includes("conflict"))) {
        appointmentSlotConflictRate.add(1);
        return;
    }
    if (status === 429) {
        trackStatus(status);
        return;
    }
    if (status >= 500) {
        appointmentServerFailureRate.add(1);
        return;
    }
    if (status !== 201 && status !== 204) {
        unexpectedStatus.add(1);
    }
}

function extractProblemCode(response) {
    if (!response || !response.body) {
        return null;
    }
    try {
        const parsed = JSON.parse(String(response.body));
        if (parsed && parsed.extensions && parsed.extensions.code) {
            return String(parsed.extensions.code);
        }
        if (parsed && parsed.code) {
            return String(parsed.code);
        }
    } catch (_error) {
        return null;
    }
    return null;
}

function sendGet({ url, accessToken, tags, trend, recordTrend = true }) {
    const response = http.get(url, {
        headers: buildHeaders(accessToken),
        tags,
    });

    trackStatus(response.status);

    if (recordTrend && trend) {
        trend.add(response.timings.duration);
    }

    return response;
}

function sendJsonRequest({ method, url, accessToken, body, tags, trend, trackWrite = false }) {
    const response = http.request(method, url, JSON.stringify(body), {
        headers: buildHeaders(accessToken, true),
        tags,
    });

    if (trackWrite) {
        trackWriteStatus(response.status, extractProblemCode(response));
    } else {
        trackStatus(response.status);
    }

    if (trend) {
        trend.add(response.timings.duration);
    }

    return response;
}

function pickWeightedReadScenario() {
    const roll = Math.random() * 100;
    let cumulative = 0;

    for (const scenario of READ_SCENARIOS) {
        cumulative += scenario.weight;
        if (roll < cumulative) {
            return scenario;
        }
    }

    return READ_SCENARIOS[READ_SCENARIOS.length - 1];
}

function shouldRunWriteIteration() {
    return Math.random() * 100 < WRITE_PERCENT;
}

function pickListPage() {
    const roll = Math.random();
    if (roll < 0.55) {
        return 1;
    }
    if (roll < 0.8) {
        return 2;
    }
    if (roll < 0.92) {
        return 3;
    }
    return 4;
}

function buildCalendarRange() {
    const now = new Date();
    const from = new Date(
        Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), 0, 0, 0, 0)
    );
    from.setUTCDate(from.getUTCDate() - 7);

    const to = new Date(from);
    to.setUTCDate(to.getUTCDate() + 30);

    return {
        dateFromUtc: from.toISOString(),
        dateToUtc: to.toISOString(),
    };
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

function buildDashboardUrl(baseUrl) {
    return `${baseUrl}/api/v1/dashboard/summary`;
}

function buildAppointmentListUrl(baseUrl, page) {
    const query = buildQueryString({ page, pageSize: LIST_PAGE_SIZE });
    return `${baseUrl}/api/v1/appointments?${query}`;
}

function buildAppointmentCalendarUrl(baseUrl, dateFromUtc, dateToUtc) {
    const query = buildQueryString({ dateFromUtc, dateToUtc });
    return `${baseUrl}/api/v1/appointments/calendar?${query}`;
}

function buildClientListUrl(baseUrl, page) {
    const query = buildQueryString({ page, pageSize: LIST_PAGE_SIZE });
    return `${baseUrl}/api/v1/clients?${query}`;
}

function buildPetListUrl(baseUrl, page) {
    const query = buildQueryString({ page, pageSize: LIST_PAGE_SIZE });
    return `${baseUrl}/api/v1/pets?${query}`;
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

function validateDashboardShape(body) {
    if (!body || typeof body !== "object") {
        return "yanit nesne degil";
    }
    if (typeof body.todayAppointmentsCount !== "number") {
        return "todayAppointmentsCount eksik veya sayi degil";
    }
    if (!Array.isArray(body.upcomingAppointments)) {
        return "upcomingAppointments dizisi eksik";
    }
    if (!Array.isArray(body.recentClients)) {
        return "recentClients dizisi eksik";
    }
    if (!Array.isArray(body.recentPets)) {
        return "recentPets dizisi eksik";
    }
    if (!Array.isArray(body.last7DaysAppointments)) {
        return "last7DaysAppointments dizisi eksik";
    }
    return null;
}

function validatePagedShape(body) {
    if (!body || typeof body !== "object") {
        return "yanit nesne degil";
    }
    if (!Array.isArray(body.items)) {
        return "items dizisi eksik";
    }
    if (typeof body.page !== "number") {
        return "page eksik veya sayi degil";
    }
    if (typeof body.pageSize !== "number") {
        return "pageSize eksik veya sayi degil";
    }
    if (typeof body.totalItems !== "number") {
        return "totalItems eksik veya sayi degil";
    }
    if (typeof body.totalPages !== "number") {
        return "totalPages eksik veya sayi degil";
    }
    return null;
}

function validateCalendarShape(body) {
    if (!Array.isArray(body)) {
        return "yanit dizi degil";
    }
    return null;
}

function assertPreflightStatus(slot, endpoint, response) {
    if (response.status === 401) {
        throw new Error(`Preflight slot ${slot} / ${endpoint}: 401 Unauthorized`);
    }
    if (response.status === 403) {
        throw new Error(`Preflight slot ${slot} / ${endpoint}: 403 Forbidden`);
    }
    if (response.status === 404) {
        throw new Error(`Preflight slot ${slot} / ${endpoint}: 404 Not Found`);
    }
    if (response.status === 429) {
        throw new Error(`Preflight slot ${slot} / ${endpoint}: 429 Too Many Requests`);
    }
    if (response.status !== 200) {
        throw new Error(
            `Preflight slot ${slot} / ${endpoint}: beklenmeyen status ${response.status}`
        );
    }
}

function preflightEndpoint(session, endpoint, url, shapeValidator) {
    const response = sendGet({
        url,
        accessToken: session.accessToken,
        tags: { endpoint, phase: "preflight", clinic_slot: session.slot },
        recordTrend: false,
    });

    assertPreflightStatus(session.slot, endpoint, response);

    let body;
    try {
        body = response.json();
    } catch (_error) {
        throw new Error(`Preflight slot ${session.slot} / ${endpoint}: JSON parse edilemedi`);
    }

    const shapeError = shapeValidator(body);
    if (shapeError) {
        throw new Error(
            `Preflight slot ${session.slot} / ${endpoint}: response shape uyumsuz (${shapeError})`
        );
    }
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

function computeMaxLinearSlotForVus(vus) {
    const maxBlockIndex =
        (vus - 1) * SLOT_RING_PER_VU + (SLOT_RING_PER_VU - 1);
    return maxBlockIndex * LIFECYCLE_BLOCK_SLOTS + RESCHEDULE_PHASE_OFFSET;
}

function countAvailableLinearSlotsWithinWindow() {
    const now = Date.now();
    const maxFutureMs = now + MAX_FUTURE_YEARS * 365 * 24 * 60 * 60 * 1000;
    let available = 0;
    let dayOffset = BASE_DAY_OFFSET;

    for (let guard = 0; guard < 5000; guard++) {
        const cursor = new Date();
        cursor.setUTCHours(0, 0, 0, 0);
        cursor.setUTCDate(cursor.getUTCDate() + dayOffset);

        const dayOfWeek = getIstanbulDayOfWeek(cursor);
        const capacity = getWeekdaySlotCapacity(dayOfWeek);

        if (capacity > 0) {
            const probe = istanbulLocalDateTimeToUtc(cursor, WORK_START_LOCAL_MINUTES);
            if (probe.getTime() > maxFutureMs) {
                break;
            }
            available += capacity;
        }

        dayOffset += 1;
        if (dayOffset > BASE_DAY_OFFSET + MAX_FUTURE_YEARS * 365) {
            break;
        }
    }

    return available;
}

function validateScheduleCapacity(vus) {
    const requiredMaxSlot = computeMaxLinearSlotForVus(vus);
    const available = countAvailableLinearSlotsWithinWindow();

    if (requiredMaxSlot >= available) {
        throw new Error(
            `VUS (${vus}) schedule kapasitesini asiyor. ` +
                `Gerekli max linear slot: ${requiredMaxSlot}, ` +
                `pencere ici kapasite: ${available} ` +
                `(SLOT_RING_PER_VU=${SLOT_RING_PER_VU}, ` +
                `LIFECYCLE_BLOCK_SLOTS=${LIFECYCLE_BLOCK_SLOTS}).`
        );
    }
}

function buildBlockIndex(vu, writeSeq) {
    return (vu - 1) * SLOT_RING_PER_VU + (writeSeq % SLOT_RING_PER_VU);
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

function buildWriteSchedule(vu, writeSeq) {
    const blockIndex = buildBlockIndex(vu, writeSeq);
    const createLinearSlot = blockIndex * LIFECYCLE_BLOCK_SLOTS;
    const rescheduleLinearSlot = createLinearSlot + RESCHEDULE_PHASE_OFFSET;

    const createScheduledAtUtc = buildScheduledAtUtcFromLinearSlot(createLinearSlot);
    const rescheduleScheduledAtUtc =
        buildScheduledAtUtcFromLinearSlot(rescheduleLinearSlot);

    if (!createScheduledAtUtc || !rescheduleScheduledAtUtc) {
        return null;
    }

    return { createScheduledAtUtc, rescheduleScheduledAtUtc };
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

function buildLoadTestNote(vu, writeSeq) {
    return `${LOAD_TEST_NOTE_PREFIX} vu=${vu} write=${writeSeq} ts=${Date.now()}`;
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

function resolvePetIdsForSession(session) {
    const response = sendGet({
        url: buildPetListUrl(CONFIG.baseUrl, 1),
        accessToken: session.accessToken,
        tags: {
            endpoint: "pet_list",
            phase: "setup",
            clinic_slot: session.slot,
        },
        recordTrend: false,
    });

    if (response.status !== 200) {
        throw new Error(
            `Setup slot ${session.slot}: pet listesi alinamadi (HTTP ${response.status}).`
        );
    }

    let body;
    try {
        body = response.json();
    } catch (_error) {
        throw new Error(`Setup slot ${session.slot}: pet listesi JSON parse edilemedi.`);
    }

    const shapeError = validatePagedShape(body);
    if (shapeError) {
        throw new Error(
            `Setup slot ${session.slot}: pet listesi response shape uyumsuz (${shapeError}).`
        );
    }

    if (!body.items || body.items.length === 0) {
        throw new Error(
            `Setup slot ${session.slot}: klinik kapsaminda kullanilabilir pet bulunamadi.`
        );
    }

    const petIds = body.items
        .map((item) => item && item.id)
        .filter((id) => id !== null && id !== undefined && String(id).trim() !== "")
        .map((id) => String(id));

    if (petIds.length === 0) {
        throw new Error(`Setup slot ${session.slot}: pet id listesi bos.`);
    }

    return petIds;
}

function resolvePetIdForVu(petIds) {
    const petIndex = (__VU - 1) % petIds.length;
    return petIds[petIndex];
}

function buildScenarioUrl(scenarioKey, calendarRange) {
    const page = pickListPage();

    switch (scenarioKey) {
        case "dashboard":
            return buildDashboardUrl(CONFIG.baseUrl);
        case "appointment_list":
            return buildAppointmentListUrl(CONFIG.baseUrl, page);
        case "appointment_calendar":
            return buildAppointmentCalendarUrl(
                CONFIG.baseUrl,
                calendarRange.dateFromUtc,
                calendarRange.dateToUtc
            );
        case "client_list":
            return buildClientListUrl(CONFIG.baseUrl, page);
        case "pet_list":
            return buildPetListUrl(CONFIG.baseUrl, page);
        default:
            throw new Error(`Bilinmeyen senaryo: ${scenarioKey}`);
    }
}

function runReadIteration(session, calendarRange) {
    const scenario = pickWeightedReadScenario();
    const url = buildScenarioUrl(scenario.key, calendarRange);

    clinicSlotRequests.add(1, { clinic_slot: session.slot });

    const response = sendGet({
        url,
        accessToken: session.accessToken,
        tags: {
            endpoint: scenario.tag,
            phase: "load",
            clinic_slot: session.slot,
        },
        trend: scenario.trend,
    });

    const passed = check(response, {
        "HTTP 200": (r) => r.status === 200,
        "401 degil": (r) => r.status !== 401,
        "403 degil": (r) => r.status !== 403,
        "429 degil": (r) => r.status !== 429,
        "5 saniyeden hizli": (r) => r.timings.duration < 5000,
    });

    if (!passed) {
        endpointCheckFailures.add(1);
    }
}

function logCreateFailure(response) {
    if (response.status === 201) {
        return;
    }

    console.error(
        `create debug vu=${__VU} writeSeq=${writeSequence} status=${response.status} body=${String(response.body || "")}`
    );
}

function logCancelFailure(response) {
    if (response.status === 204) {
        return;
    }

    console.error(
        `cancel debug vu=${__VU} writeSeq=${writeSequence} status=${response.status} body=${String(response.body || "")}`
    );
}

function attemptCancel(session, appointmentId, currentWriteSequence) {
    clinicSlotRequests.add(1, { clinic_slot: session.slot });

    const cancelResponse = sendJsonRequest({
        method: "POST",
        url: buildAppointmentCancelUrl(CONFIG.baseUrl, appointmentId),
        accessToken: session.accessToken,
        body: {
            reason: `${LOAD_TEST_NOTE_PREFIX} cancel vu=${__VU} write=${currentWriteSequence}`,
        },
        tags: {
            endpoint: "appointment_cancel",
            phase: "load",
            clinic_slot: session.slot,
        },
        trend: appointmentCancelDuration,
        trackWrite: true,
    });

    check(cancelResponse, {
        "cancel HTTP 204": (r) => r.status === 204,
        "cancel 401 degil": (r) => r.status !== 401,
        "cancel 403 degil": (r) => r.status !== 403,
        "cancel 429 degil": (r) => r.status !== 429,
        "cancel 5 saniyeden hizli": (r) => r.timings.duration < 5000,
    });

    if (cancelResponse.status === 204) {
        appointmentCancelSuccess.add(1);
        return;
    }

    logCancelFailure(cancelResponse);
    appointmentCleanupFailures.add(1);
}

function runWriteLifecycle(session, petIds) {
    const currentWriteSequence = writeSequence;
    writeSequence += 1;

    const schedule = buildWriteSchedule(__VU, currentWriteSequence);
    if (!schedule) {
        appointmentScheduleGenerationFailures.add(1);
        return;
    }

    const petId = resolvePetIdForVu(petIds);
    const note = buildLoadTestNote(__VU, currentWriteSequence);
    let appointmentId = null;

    try {
        clinicSlotRequests.add(1, { clinic_slot: session.slot });

        const createResponse = sendJsonRequest({
            method: "POST",
            url: buildAppointmentCreateUrl(CONFIG.baseUrl),
            accessToken: session.accessToken,
            body: {
                petId,
                scheduledAtUtc: schedule.createScheduledAtUtc,
                appointmentType: APPOINTMENT_TYPE_EXAMINATION,
                notes: note,
            },
            tags: {
                endpoint: "appointment_create",
                phase: "load",
                clinic_slot: session.slot,
            },
            trend: appointmentCreateDuration,
            trackWrite: true,
        });

        const createStatusSucceeded = createResponse.status === 201;

        check(createResponse, {
            "create HTTP 201": (r) => r.status === 201,
            "create 401 degil": (r) => r.status !== 401,
            "create 403 degil": (r) => r.status !== 403,
            "create 429 degil": (r) => r.status !== 429,
            "create 5 saniyeden hizli": (r) => r.timings.duration < 5000,
        });

        if (!createStatusSucceeded) {
            logCreateFailure(createResponse);
            appointmentWriteFailures.add(1);
            return;
        }

        appointmentCreateSuccess.add(1);

        appointmentId = parseCreatedAppointmentId(createResponse);
        if (!appointmentId) {
            appointmentWriteFailures.add(1);
            return;
        }

        clinicSlotRequests.add(1, { clinic_slot: session.slot });

        const rescheduleResponse = sendJsonRequest({
            method: "POST",
            url: buildAppointmentRescheduleUrl(CONFIG.baseUrl, appointmentId),
            accessToken: session.accessToken,
            body: {
                scheduledAtUtc: schedule.rescheduleScheduledAtUtc,
            },
            tags: {
                endpoint: "appointment_reschedule",
                phase: "load",
                clinic_slot: session.slot,
            },
            trend: appointmentRescheduleDuration,
            trackWrite: true,
        });

        const rescheduleStatusSucceeded = rescheduleResponse.status === 204;

        check(rescheduleResponse, {
            "reschedule HTTP 204": (r) => r.status === 204,
            "reschedule 401 degil": (r) => r.status !== 401,
            "reschedule 403 degil": (r) => r.status !== 403,
            "reschedule 429 degil": (r) => r.status !== 429,
            "reschedule 5 saniyeden hizli": (r) => r.timings.duration < 5000,
        });

        if (!rescheduleStatusSucceeded) {
            appointmentWriteFailures.add(1);
        }
    } finally {
        if (appointmentId) {
            attemptCancel(session, appointmentId, currentWriteSequence);
        }
    }
}

function randomThinkTimeSeconds() {
    const min = Number.isFinite(thinkTimeMin) ? thinkTimeMin : 0.5;
    const max = Number.isFinite(thinkTimeMax) ? thinkTimeMax : 1.5;
    const low = Math.min(min, max);
    const high = Math.max(min, max);
    return low + Math.random() * (high - low);
}

export function setup() {
    const calendarRange = buildCalendarRange();
    let petIds = null;

    for (const session of SESSIONS) {
        for (const checkDef of PREFLIGHT_CHECKS) {
            preflightEndpoint(
                session,
                checkDef.endpoint,
                checkDef.buildUrl(CONFIG.baseUrl, calendarRange),
                checkDef.validate
            );
        }

        const sessionPetIds = resolvePetIdsForSession(session);
        if (!petIds) {
            petIds = sessionPetIds;
        }
    }

    if (!petIds || petIds.length === 0) {
        throw new Error("Setup: tenant pet listesi bos.");
    }

    return { calendarRange, petIds };
}

export default function (data) {
    const session = resolveSessionForVu();

    if (!data.petIds || data.petIds.length === 0) {
        throw new Error(`Slot ${session.slot}: setup pet listesi bulunamadi.`);
    }

    if (shouldRunWriteIteration()) {
        runWriteLifecycle(session, data.petIds);
    } else {
        runReadIteration(session, data.calendarRange);
    }

    sleep(randomThinkTimeSeconds());
}
