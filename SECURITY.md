# Security policy

VioLens decrypts HTTPS traffic with a root certificate it creates on your
own computer, so security problems matter here more than in most apps. If
you find one, thank you for reporting it privately first.

## Reporting a vulnerability

Please **don't open a public issue** for a security problem. Instead,
either:

- use GitHub's private reporting: the **Report a vulnerability** button on
  the repository's [Security tab](https://github.com/MarkSPowell/VioLens/security), or
- email **security@violens.dev**.

Include what you found, the steps to reproduce it, and the version of
VioLens and the operating system you used. You'll get a reply within a
week, usually sooner. Once a fix is released, the release notes credit you
unless you'd rather not be named.

## What counts

For example:

- anything that exposes or misuses the root certificate's private key, or
  lets another program or user on the computer use it
- VioLens trusting a server certificate it shouldn't, or weakening the TLS
  connection to the real server
- the proxy accepting connections from other computers when it shouldn't
- an extension, script or file (SAZ, HAR) that can run code it shouldn't
  when opened
- the proxy host (`Violens.ProxyHost`) accepting commands from anything
  other than the app that started it

## Supported versions

VioLens is in preview: fixes go into the next release, and older previews
aren't patched. Please check the problem is still there in the
[latest release](https://github.com/MarkSPowell/VioLens/releases/latest).
