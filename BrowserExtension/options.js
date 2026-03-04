const hostName = document.getElementById("hostName");
const youtube = document.getElementById("youtube");
const facebook = document.getElementById("facebook");
const save = document.getElementById("save");
const health = document.getElementById("health");
const status = document.getElementById("status");
const logs = document.getElementById("logs");

async function refresh() {
  const data = await chrome.storage.local.get({
    hostName: "com.authorized.downloader.host",
    enabledSites: { youtube: true, facebook: true },
    logs: []
  });

  hostName.value = data.hostName;
  youtube.checked = !!data.enabledSites.youtube;
  facebook.checked = !!data.enabledSites.facebook;
  logs.textContent = data.logs.map((l) => `${l.at} ${l.type} ${JSON.stringify(l)}`).join("\n");
}

save.addEventListener("click", async () => {
  await chrome.storage.local.set({
    hostName: hostName.value.trim() || "com.authorized.downloader.host",
    enabledSites: {
      youtube: youtube.checked,
      facebook: facebook.checked
    }
  });

  status.textContent = "Saved";
  setTimeout(() => (status.textContent = ""), 1200);
});

health.addEventListener("click", () => {
  chrome.runtime.sendNativeMessage(
    hostName.value.trim() || "com.authorized.downloader.host",
    { type: "healthCheck", payload: { clientVersion: "1.0.0" }, requestId: crypto.randomUUID() },
    (response) => {
      if (chrome.runtime.lastError) {
        status.textContent = `Health failed: ${chrome.runtime.lastError.message}`;
        return;
      }

      status.textContent = `Health: ${response?.payload?.status || "unknown"}`;
    }
  );
});

refresh();
