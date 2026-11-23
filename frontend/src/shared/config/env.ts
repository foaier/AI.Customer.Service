function normalizeBaseUrl(value: string | undefined, name: string): string {
    const trimmed = value?.trim();
    if (!trimmed) return "";

    let parsed: URL;
    try {
        parsed = new URL(trimmed);
    } catch {
        throw new Error(`${name} must be an absolute HTTP or HTTPS URL.`);
    }

    if ((parsed.protocol !== "https:" && parsed.protocol !== "http:") || parsed.username || parsed.password) {
        throw new Error(`${name} must be an HTTP or HTTPS URL without embedded credentials.`);
    }

    return parsed.toString().replace(/\/$/, "");
}

export const runtimeConfig = Object.freeze({
    apiBaseUrl: normalizeBaseUrl(import.meta.env.VITE_API_BASE_URL, "VITE_API_BASE_URL"),
});
