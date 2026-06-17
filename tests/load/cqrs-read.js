import http from "k6/http";
import { check, sleep } from "k6";
import { Trend, Counter } from "k6/metrics";
import encoding from "k6/encoding";

const dashboardDuration = new Trend("dashboard_duration", true);
const appointmentListDuration = new Trend("appointment_list_duration", true);
const appointmentCalendarDuration = new Trend("appointment_calendar_duration", true);

const status401 = new Counter("status_401");
const status403 = new Counter("status_403");
const status429 = new Counter("status_429");
const unexpectedStatus = new Counter("unexpected_status");
const endpointCheckFailures = new Counter("endpoint_check_failures");
const clinicSlotRequests = new Counter("clinic_slot_requests");

const CLINIC_CLAIM = "clinic_id";
const LIST_PAGE_SIZE = 20;

const SCENARIOS = [
    {
        key: "dashboard",
        weight: 34,
        tag: "dashboard",
        trend: dashboardDuration,
    },
    {
        key: "appointment_list",
        weight: 33,
        tag: "appointment_list",
        trend: appointmentListDuration,
    },
    {
        key: "appointment_calendar",
        weight: 33,
        tag: "appointment_calendar",
        trend: appointmentCalendarDuration,
    },
];

const PREFLIGHT_CHECKS = [
    {
        endpoint: "dashboard",
        buildUrl: (baseUrl) => buildDashboardUrl(baseUrl),
        validate: validateDashboardShape,
    },
    {
        endpoint: "appointment_list",
        buildUrl: (baseUrl) => buildAppointmentListUrl(baseUrl, 1),
        validate: validatePagedShape,
    },
    {
        endpoint: "appointment_calendar",
        buildUrl: (baseUrl, calendarRange) =>
            buildAppointmentCalendarUrl(
                baseUrl,
                calendarRange.dateFromUtc,
                calendarRange.dateToUtc
            ),
        validate: validateCalendarShape,
    },
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

    const tokensJson = String(__ENV.VETINITY_TOKENS_JSON || "").trim();
    if (tokensJson) {
        return { raw: tokensJson, sourceLabel: "VETINITY_TOKENS_JSON" };
    }

    const singleToken = String(__ENV.VETINITY_TOKEN || "").trim();
    if (singleToken) {
        return {
            raw: JSON.stringify([{ slot: "01", accessToken: singleToken }]),
            sourceLabel: "VETINITY_TOKEN",
        };
    }

    throw new Error(
        "Token kaynagi bulunamadi. VETINITY_TOKENS_FILE, VETINITY_TOKENS_JSON veya VETINITY_TOKEN tanimlayin."
    );
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

function buildRuntimeConfig() {
    const baseUrl = resolveBaseUrl();
    const sessions = loadSessions();
    const vus = Number.parseInt(__ENV.VUS || "1", 10);

    if (!Number.isFinite(vus) || vus < 1) {
        throw new Error("VUS gecerli bir pozitif tamsayi olmalidir.");
    }

    if (vus % sessions.length !== 0) {
        throw new Error(
            `VUS (${vus}) token sayisi (${sessions.length}) ile tam bolunmelidir.`
        );
    }

    return { baseUrl, sessions, vus };
}

const CONFIG = buildRuntimeConfig();
const SESSIONS = CONFIG.sessions;

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
        dashboard_duration: ["p(95)<1000"],
        appointment_list_duration: ["p(95)<1000"],
        appointment_calendar_duration: ["p(95)<1000"],
        ...buildClinicSlotThresholds(SESSIONS),
    },
};

function resolveSessionForVu() {
    const sessionIndex = (__VU - 1) % SESSIONS.length;
    return SESSIONS[sessionIndex];
}

function buildHeaders(accessToken) {
    return {
        Authorization: `Bearer ${accessToken}`,
        Accept: "application/json",
    };
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
    if (status !== 200) {
        unexpectedStatus.add(1);
    }
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

function pickWeightedScenario() {
    const roll = Math.random() * 100;
    let cumulative = 0;

    for (const scenario of SCENARIOS) {
        cumulative += scenario.weight;
        if (roll < cumulative) {
            return scenario;
        }
    }

    return SCENARIOS[SCENARIOS.length - 1];
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
        default:
            throw new Error(`Bilinmeyen senaryo: ${scenarioKey}`);
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

    for (const session of SESSIONS) {
        for (const checkDef of PREFLIGHT_CHECKS) {
            preflightEndpoint(
                session,
                checkDef.endpoint,
                checkDef.buildUrl(CONFIG.baseUrl, calendarRange),
                checkDef.validate
            );
        }
    }

    return { calendarRange };
}

export default function (data) {
    const session = resolveSessionForVu();
    const scenario = pickWeightedScenario();
    const url = buildScenarioUrl(scenario.key, data.calendarRange);

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

    sleep(randomThinkTimeSeconds());
}
