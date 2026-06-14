import http from "k6/http";
import { check, sleep } from "k6";

export const options = {
    insecureSkipTLSVerify: true,
    vus: 1,
    duration: "30s",
    thresholds: {
        http_req_failed: ["rate<0.01"],
        http_req_duration: ["p(95)<1000"]
    }
};

export default function () {
    const response = http.get(
        `${__ENV.VETINITY_URL}/api/v1/dashboard/summary`,
        {
            headers: {
                Authorization: `Bearer ${__ENV.VETINITY_TOKEN}`,
                Accept: "application/json"
            }
        }
    );

    check(response, {
        "HTTP 200": (r) => r.status === 200,
        "429 degil": (r) => r.status !== 429,
        "5 saniyeden hizli": (r) => r.timings.duration < 5000
    });

    sleep(1);
}
