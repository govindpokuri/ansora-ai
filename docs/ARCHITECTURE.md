# Architecture

```mermaid
flowchart LR
  A[Caller output] --> B[WASAPI loopback]
  B --> C[Local Whisper]
  C --> D[Transcript / question queue]
  R[Reviewed resume] --> D
  P[Pasted problem] --> D
  D --> E[Your chat-completions endpoint]
  D --> F[Optional self-hosted PHP backend]
  F --> E
  F --> I[MySQL accounts / wallets / usage]
  E --> G[Streaming answer display]
  G --> H[Local call reference / export]
```

Desktop: C# WPF, .NET 9, NAudio, Whisper.net, PdfPig and Windows DPAPI.
The existing OverlayCopilot namespace is retained to limit unnecessary churn;
the Community binary is AnsoraAI.exe with isolated Community settings.

Backend: PHP 8.3+, PDO MySQL, cURL, SSE, email adapters, HMAC payment validation,
and transactional token reservations. Authentication and wallet enforcement
remain server-side even when a client can be modified.

Direct mode requires no hosted account/purchase; provider charges still apply.
Hosted mode authenticates against the operator's service and balance. Source
access grants no access to another operator's credentials or hosted capacity.
