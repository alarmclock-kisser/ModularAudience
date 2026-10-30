(async () => {
  if (!/^\/ma-youtube\/[a-f0-9]{64}\/$/.test(location.pathname) ||
      document.querySelector('meta[name="modularaudience-session"]')?.content !== "1") return;
  const status = document.getElementById("status");
  try {
    const result = await chrome.runtime.sendMessage({ type: "modularaudience-youtube-session" });
    if (result.error) throw new Error(result.error);
    const response = await fetch(location.pathname, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(result)
    });
    if (!response.ok) throw new Error("ModularAudience rejected the session response.");
    status.textContent = "Connected. You may close this tab and return to ModularAudience.";
  } catch (error) {
    status.textContent = error.message;
  }
})();
