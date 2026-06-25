const fallbackTeams = [
  "Argentina",
  "Australia",
  "Belgium",
  "Brazil",
  "Cameroon",
  "Canada",
  "Costa Rica",
  "Croatia",
  "Denmark",
  "Ecuador",
  "England",
  "France",
  "Germany",
  "Ghana",
  "Iran",
  "Japan",
  "Mexico",
  "Morocco",
  "Netherlands",
  "Poland",
  "Portugal",
  "Qatar",
  "Saudi Arabia",
  "Senegal",
  "Serbia",
  "South Korea",
  "Spain",
  "Switzerland",
  "Tunisia",
  "United States",
  "Uruguay",
  "Wales"
];

const eventTypes = [
  {
    id: "kickoff",
    label: "Game about to start",
    description: "Reminder when your team's match is close to kickoff."
  },
  {
    id: "breakingNews",
    label: "Breaking news",
    description: "Not available from football-data.org; keep this for future news API integration."
  },
  {
    id: "goals",
    label: "Goals",
    description: "Goal alerts when your team's score changes."
  },
  {
    id: "results",
    label: "Final results",
    description: "Full-time match result alerts."
  }
];

const storageKey = "worldCupTeamAlerts";
const pollIntervalMs = 70_000;
const kickoffWindowMs = 15 * 60_000;

const teamSelect = document.querySelector("#teamSelect");
const eventOptions = document.querySelector("#eventOptions");
const notificationsToggle = document.querySelector("#notificationsToggle");
const requestPermissionButton = document.querySelector("#requestPermissionButton");
const notificationStatus = document.querySelector("#notificationStatus");
const permissionStatus = document.querySelector("#permissionStatus");
const notificationHelp = document.querySelector("#notificationHelp");
const liveStatus = document.querySelector("#liveStatus");
const refreshLiveButton = document.querySelector("#refreshLiveButton");
const autoLiveButton = document.querySelector("#autoLiveButton");
const simulateButton = document.querySelector("#simulateButton");
const clearFeedButton = document.querySelector("#clearFeedButton");
const feedList = document.querySelector("#feedList");
const feedCount = document.querySelector("#feedCount");
const toast = document.querySelector("#toast");

let autoLiveId = null;
let availableTeams = [...fallbackTeams];

const defaultState = {
  selectedTeam: "Brazil",
  notificationsEnabled: false,
  selectedEvents: eventTypes.map((eventType) => eventType.id),
  feed: [],
  matchSnapshots: {},
  sentEventIds: []
};

let state = loadState();

function loadState() {
  const savedState = window.localStorage.getItem(storageKey);

  if (!savedState) {
    return { ...defaultState };
  }

  return {
    ...defaultState,
    ...JSON.parse(savedState)
  };
}

function saveState() {
  window.localStorage.setItem(storageKey, JSON.stringify(state));
}

function renderTeamOptions() {
  const teams = [...new Set([state.selectedTeam, ...availableTeams])].sort();
  teamSelect.innerHTML = teams
    .map((team) => `<option value="${escapeHtml(team)}">${escapeHtml(team)}</option>`)
    .join("");
  teamSelect.value = state.selectedTeam;
}

function renderEventOptions() {
  eventOptions.innerHTML = eventTypes
    .map((eventType) => {
      const checked = state.selectedEvents.includes(eventType.id) ? "checked" : "";
      return `
        <label class="check-row">
          <input type="checkbox" value="${eventType.id}" ${checked}>
          <span>
            <strong>${escapeHtml(eventType.label)}</strong>
            <small>${escapeHtml(eventType.description)}</small>
          </span>
        </label>
      `;
    })
    .join("");
}

function renderNotificationStatus() {
  notificationStatus.textContent = state.notificationsEnabled ? "On" : "Off";
  permissionStatus.textContent = getPermissionText();
  notificationHelp.textContent = getNotificationHelpText();
  notificationsToggle.checked = state.notificationsEnabled;
  requestPermissionButton.disabled = !canRequestBrowserNotifications();
}

function renderFeed() {
  feedCount.textContent = `${state.feed.length} ${state.feed.length === 1 ? "alert" : "alerts"}`;

  if (state.feed.length === 0) {
    feedList.innerHTML = `
      <li>
        <strong>No alerts yet</strong>
        <span>Start live polling or simulate a test event.</span>
      </li>
    `;
    return;
  }

  feedList.innerHTML = state.feed
    .map((event) => `
      <li>
        <strong>${escapeHtml(event.title)}</strong>
        <span>${escapeHtml(event.message)}</span>
        <time datetime="${event.createdAt}">${new Date(event.createdAt).toLocaleString()}</time>
      </li>
    `)
    .join("");
}

function getPermissionText() {
  return "Native system notifications are sent by the local server";
}

function getNotificationHelpText() {
  return "Keep node server.js running. No browser notification permission is required.";
}

function canRequestBrowserNotifications() {
  return true;
}

function createSimulatedEvent() {
  const enabledEventTypes = eventTypes.filter((eventType) =>
    state.selectedEvents.includes(eventType.id)
  );
  const possibleTypes = enabledEventTypes.length > 0 ? enabledEventTypes : eventTypes;
  const eventType = possibleTypes[Math.floor(Math.random() * possibleTypes.length)];

  const factories = {
    kickoff: () => ({
      title: `${state.selectedTeam} match starts soon`,
      message: `${state.selectedTeam} kicks off in about 15 minutes.`
    }),
    breakingNews: () => ({
      title: `${state.selectedTeam} team news`,
      message: "football-data.org does not include injury/news data; add a news API for this alert type."
    }),
    goals: () => ({
      title: `Goal alert for ${state.selectedTeam}`,
      message: `${state.selectedTeam} scored in this test event.`
    }),
    results: () => ({
      title: `${state.selectedTeam} final result`,
      message: `Full time: ${state.selectedTeam} finished with a test result.`
    })
  };

  return {
    id: `sim-${Date.now()}`,
    type: eventType.id,
    ...factories[eventType.id](),
    createdAt: new Date().toISOString()
  };
}

async function refreshLiveData() {
  refreshLiveButton.disabled = true;
  refreshLiveButton.classList.add("loading");
  liveStatus.textContent = "Checking football-data.org...";

  try {
    const { response, payload } = await fetchJson("/api/world-cup-matches");

    if (!response.ok) {
      throw new Error(payload.error || "Unable to fetch live match data.");
    }

    updateAvailableTeams(payload.matches || []);
    const events = detectMatchEvents(payload.matches || []);
    events.forEach(notify);
    saveState();
    renderTeamOptions();

    const cacheText = payload.cached ? "cached" : "fresh";
    const fetchedAt = payload.fetchedAt ? new Date(payload.fetchedAt).toLocaleTimeString() : "unknown";
    liveStatus.textContent = `Last ${cacheText} check: ${fetchedAt}. Matches returned: ${payload.matches?.length || 0}. Next safe poll: 70 seconds.`;
  } catch (error) {
    liveStatus.textContent = error.message;
    showToast({
      title: "Live data error",
      message: error.message
    });
  } finally {
    refreshLiveButton.disabled = false;
    refreshLiveButton.classList.remove("loading");
  }
}

async function fetchJson(url, options) {
  const response = await fetch(url, options);
  const contentType = response.headers.get("content-type") || "";

  if (!contentType.includes("application/json")) {
    const text = await response.text();
    const startsWithHtml = text.trimStart().startsWith("<!DOCTYPE") || text.trimStart().startsWith("<html");

    if (startsWithHtml) {
      throw new Error("Live API proxy is not running. Start it with: node server.js, then open http://localhost:8080");
    }

    throw new Error(`Expected JSON from ${url}, but received ${contentType || "an unknown content type"}.`);
  }

  return {
    response,
    payload: await response.json()
  };
}

function updateAvailableTeams(matches) {
  const teamsFromMatches = matches.flatMap((match) => [
    match.homeTeam?.name,
    match.awayTeam?.name
  ]).filter(Boolean);

  if (teamsFromMatches.length > 0) {
    availableTeams = [...new Set([...fallbackTeams, ...teamsFromMatches])];
  }
}

function detectMatchEvents(matches) {
  const events = [];

  matches
    .filter(isFavoriteMatch)
    .forEach((match) => {
      const snapshot = createMatchSnapshot(match);
      const previous = state.matchSnapshots[snapshot.id];

      events.push(...detectKickoffEvents(match, snapshot, previous));
      events.push(...detectGoalEvents(match, snapshot, previous));
      events.push(...detectResultEvents(match, snapshot, previous));

      state.matchSnapshots[snapshot.id] = snapshot;
    });

  state.sentEventIds = state.sentEventIds.slice(-200);
  return events.filter((event) => shouldDeliverEvent(event));
}

function detectKickoffEvents(match, snapshot, previous) {
  if (!state.selectedEvents.includes("kickoff")) {
    return [];
  }

  const events = [];
  const startsAt = new Date(match.utcDate).getTime();
  const startsInMs = startsAt - Date.now();
  const isUpcomingSoon = startsInMs > 0 && startsInMs <= kickoffWindowMs;
  const justWentLive = previous && !isLiveStatus(previous.status) && isLiveStatus(snapshot.status);

  if (isUpcomingSoon) {
    events.push({
      id: `${snapshot.id}-kickoff-${match.utcDate}`,
      type: "kickoff",
      title: `${state.selectedTeam} match starts soon`,
      message: `${formatMatchTitle(match)} kicks off at ${new Date(match.utcDate).toLocaleTimeString()}.`,
      createdAt: new Date().toISOString()
    });
  }

  if (justWentLive) {
    events.push({
      id: `${snapshot.id}-live-${snapshot.status}`,
      type: "kickoff",
      title: `${state.selectedTeam} match is live`,
      message: `${formatMatchTitle(match)} is now ${snapshot.status.toLowerCase().replaceAll("_", " ")}.`,
      createdAt: new Date().toISOString()
    });
  }

  return events;
}

function detectGoalEvents(match, snapshot, previous) {
  if (!state.selectedEvents.includes("goals") || !previous) {
    return [];
  }

  const homeIncreased = snapshot.homeScore !== null && previous.homeScore !== null && snapshot.homeScore > previous.homeScore;
  const awayIncreased = snapshot.awayScore !== null && previous.awayScore !== null && snapshot.awayScore > previous.awayScore;

  if (!homeIncreased && !awayIncreased) {
    return [];
  }

  return [{
    id: `${snapshot.id}-goal-${snapshot.homeScore}-${snapshot.awayScore}`,
    type: "goals",
    title: `Goal update: ${formatMatchTitle(match)}`,
    message: `Current score: ${snapshot.homeScore ?? 0}-${snapshot.awayScore ?? 0}.`,
    createdAt: new Date().toISOString()
  }];
}

function detectResultEvents(match, snapshot, previous) {
  if (!state.selectedEvents.includes("results")) {
    return [];
  }

  const finishedNow = snapshot.status === "FINISHED" && (!previous || previous.status !== "FINISHED");

  if (!finishedNow) {
    return [];
  }

  return [{
    id: `${snapshot.id}-finished-${snapshot.homeScore}-${snapshot.awayScore}`,
    type: "results",
    title: `Final result: ${formatMatchTitle(match)}`,
    message: `Full time: ${snapshot.homeScore ?? 0}-${snapshot.awayScore ?? 0}.`,
    createdAt: new Date().toISOString()
  }];
}

function shouldDeliverEvent(event) {
  if (state.sentEventIds.includes(event.id)) {
    return false;
  }

  state.sentEventIds.push(event.id);
  return true;
}

function createMatchSnapshot(match) {
  const score = getBestScore(match);

  return {
    id: String(match.id),
    status: match.status,
    utcDate: match.utcDate,
    homeScore: score.home,
    awayScore: score.away
  };
}

function getBestScore(match) {
  const score = match.score || {};
  const candidates = [
    score.fullTime,
    score.regularTime,
    score.halfTime
  ];
  const best = candidates.find((candidate) =>
    candidate && (candidate.home !== null || candidate.away !== null)
  ) || {};

  return {
    home: best.home ?? null,
    away: best.away ?? null
  };
}

function isFavoriteMatch(match) {
  return [match.homeTeam?.name, match.awayTeam?.name].includes(state.selectedTeam);
}

function isLiveStatus(status) {
  return ["LIVE", "IN_PLAY", "PAUSED"].includes(status);
}

function formatMatchTitle(match) {
  return `${match.homeTeam?.name || "Home"} vs ${match.awayTeam?.name || "Away"}`;
}

function addEventToFeed(event) {
  state.feed = [event, ...state.feed].slice(0, 20);
  saveState();
  renderFeed();
}

function notify(event) {
  addEventToFeed(event);
  showToast(event);

  if (!state.notificationsEnabled) {
    return;
  }

  sendNativeNotification(event);
}

function showToast(event) {
  toast.innerHTML = `<strong>${escapeHtml(event.title)}</strong><span>${escapeHtml(event.message)}</span>`;
  toast.hidden = false;
  window.clearTimeout(showToast.timeoutId);
  showToast.timeoutId = window.setTimeout(() => {
    toast.hidden = true;
  }, 5500);
}

function simulateEvent() {
  notify(createSimulatedEvent());
}

function toggleLivePolling() {
  if (autoLiveId) {
    window.clearInterval(autoLiveId);
    autoLiveId = null;
    autoLiveButton.textContent = "Start live polling";
    return;
  }

  refreshLiveData();
  autoLiveId = window.setInterval(refreshLiveData, pollIntervalMs);
  autoLiveButton.textContent = "Stop live polling";
}

async function requestNotificationPermission() {
  const delivered = await sendNativeNotification({
    title: "World Cup Team Alerts",
    message: "Native system notifications are working."
  });

  if (delivered) {
    showToast({
      title: "Notification sent",
      message: "Windows accepted the native notification request."
    });
  }

  renderNotificationStatus();
  return delivered;
}

async function sendNativeNotification(event) {
  try {
    const { response, payload } = await fetchJson("/api/native-notification", {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify({
        title: event.title,
        message: event.message
      })
    });

    if (!response.ok) {
      throw new Error(payload.error || "Native notification failed.");
    }

    return true;
  } catch (error) {
    liveStatus.textContent = error.message;
    showToast({
      title: "Native notification error",
      message: error.message
    });
    return false;
  }
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

teamSelect.addEventListener("change", (event) => {
  state.selectedTeam = event.target.value;
  saveState();
});

eventOptions.addEventListener("change", () => {
  state.selectedEvents = [...eventOptions.querySelectorAll("input:checked")].map(
    (input) => input.value
  );
  saveState();
});

notificationsToggle.addEventListener("change", (event) => {
  state.notificationsEnabled = event.target.checked;

  saveState();
  renderNotificationStatus();
});

requestPermissionButton.addEventListener("click", requestNotificationPermission);
refreshLiveButton.addEventListener("click", refreshLiveData);
autoLiveButton.addEventListener("click", toggleLivePolling);
simulateButton.addEventListener("click", simulateEvent);
clearFeedButton.addEventListener("click", () => {
  state.feed = [];
  saveState();
  renderFeed();
});

renderTeamOptions();
renderEventOptions();
renderNotificationStatus();
renderFeed();
