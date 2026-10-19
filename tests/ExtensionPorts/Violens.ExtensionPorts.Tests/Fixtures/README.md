# Fixtures

Synthetic Chromium NetLog captures, written by hand for these tests. They
contain no real browsing data: every host is under `.test`, and every
header and body is made up.

Real browser captures must never be committed here. They routinely contain
cookies, tokens and personal data.

`netlog-basic.json` has two URL requests:

- `GET https://example.test/hello?x=1` over HTTP/1.1, with a traffic
  annotation, response headers that claim gzip, and a 14-byte decoded body
  ("Hello, NetLog!").
- `POST https://api.example.test/submit` over HTTP/2 (pseudo-headers, no
  Host header), with a 5-byte request body the NetLog doesn't include, and
  a 404 whose 42-byte body the NetLog also didn't capture.

One event per line, so the truncated-file test can cut the file at a line
boundary the way a real truncated capture ends.
