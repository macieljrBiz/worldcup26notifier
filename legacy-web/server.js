const http = require("http");
const fs = require("fs");
const path = require("path");
const { spawn } = require("child_process");

const port = Number(process.env.PORT || 8080);
const rootDir = __dirname;
const cacheTtlMs = 70_000;

loadDotEnv();

const competitionCode = process.env.FOOTBALL_DATA_COMPETITION || "WC";

let cachedPayload = null;
let cachedAt = 0;

const server = http.createServer(async (request, response) => {
  try {
    const url = new URL(request.url, `http://${request.headers.host}`);

    if (url.pathname === "/api/world-cup-matches") {
      await handleMatches(response);
      return;
    }

    if (url.pathname === "/api/native-notification" && request.method === "POST") {
      await handleNativeNotification(request, response);
      return;
    }

    serveStaticFile(url.pathname, response);
  } catch (error) {
    sendJson(response, 500, { error: error.message });
  }
});

server.listen(port, () => {
  console.log(`World Cup Team Alerts running at http://localhost:${port}`);
});

async function handleMatches(response) {
  const token = process.env.FOOTBALL_DATA_API_KEY;

  if (!token) {
    sendJson(response, 500, {
      error: "Missing FOOTBALL_DATA_API_KEY. Add it to .env or set it as an environment variable."
    });
    return;
  }

  const now = Date.now();

  if (cachedPayload && now - cachedAt < cacheTtlMs) {
    sendJson(response, 200, {
      ...cachedPayload,
      cached: true
    });
    return;
  }

  const dateFrom = formatDate(new Date(now - 24 * 60 * 60 * 1000));
  const dateTo = formatDate(new Date(now + 14 * 24 * 60 * 60 * 1000));
  const apiUrl = `https://api.football-data.org/v4/competitions/${competitionCode}/matches?dateFrom=${dateFrom}&dateTo=${dateTo}`;

  const apiResponse = await fetch(apiUrl, {
    headers: {
      "X-Auth-Token": token
    }
  });

  const payload = await apiResponse.json();

  if (!apiResponse.ok) {
    sendJson(response, apiResponse.status, {
      error: payload.message || `football-data.org returned HTTP ${apiResponse.status}`
    });
    return;
  }

  cachedPayload = {
    competition: payload.competition,
    matches: payload.matches || [],
    fetchedAt: new Date().toISOString(),
    cached: false,
    rateLimit: {
      configuredPollSeconds: Math.round(cacheTtlMs / 1000),
      note: "Server caches responses to stay under 10 calls per minute."
    }
  };
  cachedAt = now;

  sendJson(response, 200, cachedPayload);
}

async function handleNativeNotification(request, response) {
  const body = await readJsonBody(request);
  const title = normalizeNotificationText(body.title, "World Cup Team Alerts", 80);
  const message = normalizeNotificationText(body.message, "New team alert", 220);

  await showNativeNotification(title, message);
  sendJson(response, 200, { delivered: true, method: "windows-toast" });
}

function serveStaticFile(urlPath, response) {
  const safePath = urlPath === "/" ? "/index.html" : urlPath;
  const filePath = path.normalize(path.join(rootDir, safePath));

  if (!filePath.startsWith(rootDir)) {
    response.writeHead(403);
    response.end("Forbidden");
    return;
  }

  fs.readFile(filePath, (error, content) => {
    if (error) {
      response.writeHead(404);
      response.end("Not found");
      return;
    }

    response.writeHead(200, {
      "Content-Type": getContentType(filePath),
      "Cache-Control": "no-store"
    });
    response.end(content);
  });
}

function readJsonBody(request) {
  return new Promise((resolve, reject) => {
    let body = "";

    request.on("data", (chunk) => {
      body += chunk;

      if (body.length > 10_000) {
        reject(new Error("Request body is too large."));
        request.destroy();
      }
    });

    request.on("end", () => {
      try {
        resolve(body ? JSON.parse(body) : {});
      } catch {
        reject(new Error("Request body must be valid JSON."));
      }
    });

    request.on("error", reject);
  });
}

function showNativeNotification(title, message) {
  if (process.platform !== "win32") {
    throw new Error("Native notifications are currently implemented for Windows only.");
  }

  const script = `
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] > $null
$title = [System.Security.SecurityElement]::Escape($env:WC_ALERT_TITLE)
$message = [System.Security.SecurityElement]::Escape($env:WC_ALERT_MESSAGE)
$xmlText = "<toast><visual><binding template='ToastGeneric'><text>$title</text><text>$message</text></binding></visual></toast>"
$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
$xml.LoadXml($xmlText)
$toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
$notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier("World Cup Team Alerts")
$notifier.Show($toast)
`;

  return new Promise((resolve, reject) => {
    const child = spawn("powershell.exe", [
      "-NoProfile",
      "-ExecutionPolicy",
      "Bypass",
      "-Command",
      script
    ], {
      env: {
        ...process.env,
        WC_ALERT_TITLE: title,
        WC_ALERT_MESSAGE: message
      },
      windowsHide: true
    });

    let stderr = "";
    child.stderr.on("data", (chunk) => {
      stderr += chunk.toString();
    });
    child.on("error", reject);
    child.on("exit", (code) => {
      if (code === 0) {
        resolve();
        return;
      }

      reject(new Error(stderr.trim() || `PowerShell notification exited with code ${code}.`));
    });
  });
}

function normalizeNotificationText(value, fallback, maxLength) {
  const normalized = String(value || fallback).replace(/\s+/g, " ").trim();
  return normalized.slice(0, maxLength);
}

function sendJson(response, statusCode, payload) {
  response.writeHead(statusCode, {
    "Content-Type": "application/json",
    "Cache-Control": "no-store"
  });
  response.end(JSON.stringify(payload));
}

function loadDotEnv() {
  const envPath = path.join(rootDir, ".env");

  if (!fs.existsSync(envPath)) {
    return;
  }

  const envFile = fs.readFileSync(envPath, "utf8");
  envFile.split(/\r?\n/).forEach((line) => {
    const trimmed = line.trim();

    if (!trimmed || trimmed.startsWith("#")) {
      return;
    }

    const separatorIndex = trimmed.indexOf("=");

    if (separatorIndex === -1) {
      return;
    }

    const key = trimmed.slice(0, separatorIndex).trim();
    const value = trimmed.slice(separatorIndex + 1).trim().replace(/^["']|["']$/g, "");

    if (!process.env[key]) {
      process.env[key] = value;
    }
  });
}

function formatDate(date) {
  return date.toISOString().slice(0, 10);
}

function getContentType(filePath) {
  const extension = path.extname(filePath).toLowerCase();

  return {
    ".html": "text/html; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".js": "application/javascript; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".ico": "image/x-icon"
  }[extension] || "application/octet-stream";
}
