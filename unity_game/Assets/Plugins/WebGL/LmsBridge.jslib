mergeInto(LibraryManager.library, {
  // Called from EliminationZone.cs when the player falls in Last Man Standing.
  // Dispatches to the JS shell, which returns to the lobby (same path as the "← Menu" button).
  LmsGameOver: function () {
    if (typeof window !== 'undefined' && typeof window.__unityGameOver === 'function') {
      window.__unityGameOver();
    }
  },

  // Called from MatchReporter.cs when the local player's match ends (multiplayer v1). Forwards the
  // result {mode, survivalMs, finished} to the JS shell, which reports it to the lobby server.
  MatchResult: function (ptr) {
    var s = UTF8ToString(ptr);
    if (typeof window !== 'undefined' && typeof window.__unityMatchResult === 'function') {
      try { window.__unityMatchResult(JSON.parse(s)); } catch (e) { console.error('MatchResult parse', e); }
    }
  },

  // Synchronized start: called from IntroCountdown.cs when the gameplay scene is loaded + frozen.
  // Tells the JS shell to report "ready" to the server; the server replies beginCountdown to all.
  NetReady: function () {
    if (typeof window !== 'undefined' && typeof window.__unityReady === 'function') {
      window.__unityReady();
    }
  },

  // Live avatars: called from AvatarSync.cs ~15 Hz with the local bean's pose JSON.
  // Forwarded to the JS shell, which wraps it as a `state` message to the server.
  NetSend: function (ptr) {
    var s = UTF8ToString(ptr);
    if (typeof window !== 'undefined' && typeof window.__unityNetSend === 'function') {
      window.__unityNetSend(s);
    }
  },

  // Called from IntroCountdown.cs once per tick ("3","2","1","GO") and once more with "" to clear.
  // Drives the beefy DOM overlay (web/src/ui/countdown.ts). Unity owns the timing; this is purely visual.
  CountdownTick: function (ptr) {
    var s = UTF8ToString(ptr);
    if (typeof window !== 'undefined' && typeof window.__unityCountdown === 'function') {
      window.__unityCountdown(s);
    }
  },

  // Called from SpinnerDifficultyRamp.cs on each difficulty event ("BOTH REVERSE", "BIG BEAM ⚡", …).
  // Drives the small high-positioned toast (web/src/ui/countdown.ts) so escalation reads on screen.
  GameToast: function (ptr) {
    var s = UTF8ToString(ptr);
    if (typeof window !== 'undefined' && typeof window.__unityToast === 'function') {
      window.__unityToast(s);
    }
  }
});
