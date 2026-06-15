import http from "k6/http";
import { check, sleep } from "k6";
import { Trend, Counter } from "k6/metrics";

const dashboardDuration = new Trend("dashboard_duration", true);
const appointmentListDuration = new Trend("appointment_list_duration", true);
const appointmentCalendarDuration = new Trend("appointment_calendar_duration", true);
const clientListDuration = new Trend("client_list_duration", true);
const petListDuration = new Trend("pet_list_duration", true);

const dashboardResponseBytes = new Trend("dashboard_response_bytes", false);
const appointmentListResponseBytes = new Trend("appointment_list_response_bytes", false);
const appointmentCalendarResponseBytes = new Trend("appointment_calendar_response_bytes", false);
const clientListResponseBytes = new Trend("client_list_response_bytes", false);
const petListResponseBytes = new Trend("pet_list_response_bytes", false);

const status401 = new Counter("status_401");
const status403 = new Counter("status_403");
const status429 = new Counter("status_429");
const unexpectedStatus = new Counter("unexpected_status");
const endpointCheckFailures = new Counter("endpoint_check_failures");

const LIST_PAGE_SIZE = 20;

const SCENARIOS = [
    {
        key: "dashboard",
        weight: 25,
        tag: "dashboard",
        trend: dashboardDuration,
        responseBytes: dashboardResponseBytes,
    },
    {
        key: "appointment_list",
        weight: 25,
        tag: "appointment_list",
        trend: appointmentListDuration,
        responseBytes: appointmentListResponseBytes,
    },
    {
        key: "appointment_calendar",
        weight: 20,
        tag: "appointment_calendar",
        trend: appointmentCalendarDuration,
        responseBytes: appointmentCalendarResponseBytes,
    },
    {
        key: "client_list",
        weight: 15,
        tag: "client_list",
        trend: clientListDuration,
        responseBytes: clientListResponseBytes,
    },
    {
        key: "pet_list",
        weight: 15,
        tag: "pet_list",
        trend: petListDuration,
        responseBytes: petListResponseBytes,
    },
];

function validateRequiredEnv() {
    const rawUrl = __ENV.VETINITY_URL;
    const token = __ENV.VETINITY_TOKEN;

    if (!rawUrl || String(rawUrl).trim() === "") {
        throw new Error(
            "VETINITY_URL ortam degiskeni zorunludur. Ornek: VETINITY_URL=https://localhost:7001"
        );
    }

    if (!token || String(token).trim() === "") {
        throw new Error("VETINITY_TOKEN ortam degiskeni zorunludur.");
    }

    return {
        baseUrl: String(rawUrl).replace(/\/$/, ""),
        token: String(token).trim(),
    };
}

const ENV = validateRequiredEnv();

const vus = Number.parseInt(__ENV.VUS || "1", 10);
const duration = __ENV.DURATION || "30s";
const thinkTimeMin = Number.parseFloat(__ENV.THINK_TIME_MIN || "0.5");
const thinkTimeMax = Number.parseFloat(__ENV.THINK_TIME_MAX || "1.5");

export const options = {
    insecureSkipTLSVerify: true,
    vus,
    duration,
    thresholds: {
        http_req_failed: ["rate<0.01"],
        checks: ["rate>0.99"],
        http_req_duration: ["p(95)<1000", "p(99)<2000"],
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
    },
};

function buildHeaders(token) {
    return {
        Authorization: `Bearer ${token}`,
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

function utf8ByteLength(value) {
    if (value === null || value === undefined) {
        return 0;
    }

    const text = String(value);
    if (text.length === 0) {
        return 0;
    }

    let bytes = 0;

    for (let i = 0; i < text.length; i++) {
        let codePoint = text.charCodeAt(i);

        if (codePoint >= 0xd800 && codePoint <= 0xdbff && i + 1 < text.length) {
            const low = text.charCodeAt(i + 1);
            if (low >= 0xdc00 && low <= 0xdfff) {
                codePoint = 0x10000 + ((codePoint - 0xd800) << 10) + (low - 0xdc00);
                i++;
            }
        }

        if (codePoint <= 0x7f) {
            bytes += 1;
        } else if (codePoint <= 0x7ff) {
            bytes += 2;
        } else if (codePoint <= 0xffff) {
            bytes += 3;
        } else {
            bytes += 4;
        }
    }

    return bytes;
}

function sendGet({ url, token, tags, trend, responseBytesTrend, recordTrend = true }) {
    const response = http.get(url, {
        headers: buildHeaders(token),
        tags,
    });

    trackStatus(response.status);

    if (recordTrend && trend) {
        trend.add(response.timings.duration);
    }

    if (recordTrend && responseBytesTrend) {
        responseBytesTrend.add(utf8ByteLength(response.body));
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

function buildClientListUrl(baseUrl, page) {
    const query = buildQueryString({ page, pageSize: LIST_PAGE_SIZE });
    return `${baseUrl}/api/v1/clients?${query}`;
}

function buildPetListUrl(baseUrl, page) {
    const query = buildQueryString({ page, pageSize: LIST_PAGE_SIZE });
    return `${baseUrl}/api/v1/pets?${query}`;
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

function assertPreflightStatus(endpoint, response) {
    if (response.status === 401) {
        throw new Error(`Preflight ${endpoint}: 401 Unauthorized`);
    }
    if (response.status === 403) {
        throw new Error(`Preflight ${endpoint}: 403 Forbidden`);
    }
    if (response.status === 404) {
        throw new Error(`Preflight ${endpoint}: 404 Not Found`);
    }
    if (response.status === 429) {
        throw new Error(`Preflight ${endpoint}: 429 Too Many Requests`);
    }
    if (response.status !== 200) {
        throw new Error(`Preflight ${endpoint}: beklenmeyen status ${response.status}`);
    }
}

function preflightEndpoint(endpoint, url, token, shapeValidator) {
    const response = sendGet({
        url,
        token,
        tags: { endpoint, phase: "preflight" },
        recordTrend: false,
    });

    assertPreflightStatus(endpoint, response);

    let body;
    try {
        body = response.json();
    } catch (_error) {
        throw new Error(`Preflight ${endpoint}: JSON parse edilemedi`);
    }

    const shapeError = shapeValidator(body);
    if (shapeError) {
        throw new Error(`Preflight ${endpoint}: response shape uyumsuz (${shapeError})`);
    }
}

function buildScenarioUrl(scenarioKey, calendarRange) {
    const page = pickListPage();

    switch (scenarioKey) {
        case "dashboard":
            return buildDashboardUrl(ENV.baseUrl);
        case "appointment_list":
            return buildAppointmentListUrl(ENV.baseUrl, page);
        case "appointment_calendar":
            return buildAppointmentCalendarUrl(
                ENV.baseUrl,
                calendarRange.dateFromUtc,
                calendarRange.dateToUtc
            );
        case "client_list":
            return buildClientListUrl(ENV.baseUrl, page);
        case "pet_list":
            return buildPetListUrl(ENV.baseUrl, page);
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

    preflightEndpoint(
        "dashboard",
        buildDashboardUrl(ENV.baseUrl),
        ENV.token,
        validateDashboardShape
    );
    preflightEndpoint(
        "appointment_list",
        buildAppointmentListUrl(ENV.baseUrl, 1),
        ENV.token,
        validatePagedShape
    );
    preflightEndpoint(
        "appointment_calendar",
        buildAppointmentCalendarUrl(
            ENV.baseUrl,
            calendarRange.dateFromUtc,
            calendarRange.dateToUtc
        ),
        ENV.token,
        validateCalendarShape
    );
    preflightEndpoint(
        "client_list",
        buildClientListUrl(ENV.baseUrl, 1),
        ENV.token,
        validatePagedShape
    );
    preflightEndpoint(
        "pet_list",
        buildPetListUrl(ENV.baseUrl, 1),
        ENV.token,
        validatePagedShape
    );

    return { calendarRange };
}

export default function (data) {
    const scenario = pickWeightedScenario();
    const url = buildScenarioUrl(scenario.key, data.calendarRange);

    const response = sendGet({
        url,
        token: ENV.token,
        tags: { endpoint: scenario.tag, phase: "load" },
        trend: scenario.trend,
        responseBytesTrend: scenario.responseBytes,
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
