# Changelog

## 2.2.1

- Fix settings content drifting to the right as the window grows.
- Constrain settings content to the available viewport in logical pixels.
- Verify settings controls at three window widths on a 125% DPI display, including mouse-wheel scrolling.
- Publish the project as LumaConvert, with build instructions and screenshots taken from the real application using generated demo media.

## 2.2.0

- Trim unused managed runtime components while retaining WinRT compatibility.
- Decode images in a short-lived worker process sharing the application's runtime.
- Keep small list thumbnails near the visible region and a separate selected-file preview.
- Limit video decoder/encoder threads and x264 lookahead buffers.
- Preserve offline, self-contained portable distribution.
