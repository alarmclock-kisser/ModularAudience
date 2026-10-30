chrome.runtime.onMessage.addListener((message, sender, respond) => {
  const url = sender.url ? new URL(sender.url) : null;
  if (message?.type !== "modularaudience-youtube-session" ||
      url?.hostname !== "127.0.0.1" || !/^\/ma-youtube\/[a-f0-9]{64}\/$/.test(url.pathname)) return;
  chrome.cookies.getAll({ domain: "youtube.com" }).then(cookies => {
    respond({ cookies: cookies.map(c => ({
      domain: c.domain, path: c.path, secure: c.secure, httpOnly: c.httpOnly,
      expirationDate: c.expirationDate ?? 0, name: c.name, value: c.value
    })) });
  }).catch(() => respond({ error: "Chrome could not read the YouTube cookies." }));
  return true;
});
