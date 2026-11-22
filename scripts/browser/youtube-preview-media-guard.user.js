// ==UserScript==
// @name         YouTube — keep preview out of Windows media controls
// @namespace    dynamic-island.local
// @version      1.0.0
// @description  Keep hover previews, without publishing their MediaSession. Normal video controls remain available.
// @match        https://www.youtube.com/*
// @run-at       document-start
// @grant        none
// @sandbox      raw
// ==/UserScript==

(() => {
  'use strict';
  if (window.__islandYouTubeMediaGuard || !navigator.mediaSession) return;
  const native = navigator.mediaSession;
  const handlers = new Map();
  const methods = new Map();
  let shadowMetadata = null;
  let shadowPlaybackState = 'none';

  function isVideoPage() {
    const url = new URL(location.href);
    return (url.pathname === '/watch' && !!url.searchParams.get('v'))
      || /^\/(shorts|live|embed)\/[^/]+/.test(url.pathname)
      // An explicitly opened video can continue in YouTube's miniplayer on the feed.
      || (typeof document !== 'undefined' && !!document.querySelector('ytd-miniplayer[active]'));
  }

  let enabled = isVideoPage();
  function syncRoute() {
    const next = isVideoPage();
    if (next === enabled) return;
    enabled = next;
    if (enabled) {
      // YouTube can register transport handlers once, before a SPA navigation.
      for (const [action, handler] of handlers) {
        try { native.setActionHandler(action, handler); } catch { /* Unsupported action. */ }
      }
      // Do not replay metadata/position belonging to a feed preview.
    } else {
      native.metadata = null;
      native.playbackState = 'none';
      try { native.setPositionState?.(); } catch { /* Older browser. */ }
      for (const action of handlers.keys()) {
        try { native.setActionHandler(action, null); } catch { /* Unsupported action. */ }
      }
      shadowMetadata = null;
      shadowPlaybackState = 'none';
    }
  }

  const facade = new Proxy(native, {
    get(target, key) {
      syncRoute();
      if (key === 'metadata') return enabled ? native.metadata : shadowMetadata;
      if (key === 'playbackState') return enabled ? native.playbackState : shadowPlaybackState;
      const value = Reflect.get(target, key, target);
      if (typeof value !== 'function') return value;
      if (!methods.has(key)) methods.set(key, (...args) => {
        syncRoute();
        if (key === 'setActionHandler') handlers.set(args[0], args[1]);
        if (enabled) return Reflect.apply(value, target, args);
        // Accept the page's preview bookkeeping without touching native control.
        if (key === 'setActionHandler' || key === 'setPositionState') return;
        return Reflect.apply(value, target, args);
      });
      return methods.get(key);
    },
    set(target, key, value) {
      syncRoute();
      if (key === 'metadata') {
        shadowMetadata = value;
        if (enabled) native.metadata = value;
        return true;
      }
      if (key === 'playbackState') {
        shadowPlaybackState = value;
        if (enabled) native.playbackState = value;
        return true;
      }
      return Reflect.set(target, key, value, target);
    },
  });

  try {
    Object.defineProperty(navigator, 'mediaSession', {
      configurable: true,
      enumerable: true,
      get: () => facade,
    });
  } catch {
    // Leave native behaviour intact if this browser disallows the wrapper.
    return;
  }
  Object.defineProperty(window, '__islandYouTubeMediaGuard', {
    configurable: true,
    value: { version: '1.0.0', get enabled() { return enabled; } },
  });
  window.addEventListener('yt-navigate-finish', syncRoute);
  window.addEventListener('popstate', syncRoute);
  // No audio-level threshold, play/pause calls, network traffic or persistent history.
})();
