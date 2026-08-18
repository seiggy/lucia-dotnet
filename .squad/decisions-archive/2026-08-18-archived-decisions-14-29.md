# Squad Decisions Archive — 2026-08-18

**Archived:** Decisions 14-29 (dated 2026-05-30 through 2026-07-18, older than 30 days from 2026-08-18)

These decisions have been archived. See .squad/decisions/decisions.md and .squad/decisions/worklog.md for historical context.

## Archived Decisions

### 25. Aspire 13.4 Redis — Disable Client Certificate Trust Scope (Parker, 2026-07-01)

**Summary:** Aspire.Hosting 13.4.2 split certificate handling into server-HTTPS and client-trust APIs. Redis was reported UNHEALTHY in dashboard. Fix: added .WithCertificateTrustScope(CertificateTrustScope.None) to Redis chain in lucia.AppHost/AppHost.cs.


### 24. Transitive Package Vulnerability Pins (Parker, 2026-07-01)

**Summary:** Pinned three vulnerable transitive dependencies in Directory.Packages.props.


### 14-23. Frontend & Security Decisions (2026-05-30)

Archived decisions covering WebSocket token validation, constant-time auth comparison, React error boundary, pipeline timing snapshots, GitHub Actions pinning, URI validation, Docker digest pinning, mDNS alignment, and service YAML documentation.


### 26. Hire Vasquez as PR Review Gatekeeper (Squad, 2026-07-10)

**Summary:** Owner hired Vasquez (model-locked gpt-5.6-sol) as dedicated review agent. Established mandatory pre-push review gate for squad/* branches.


### 27. Jetson Orin Nano Native Voice Inference (Ripley, 2026-07-17)

**Summary:** Recommended architecture: C# Wyoming host over native C ABI (CUDA-accelerated sherpa-onnx + ONNX Runtime GPU). Target: Jetson Orin Nano Super 8GB.


### 28. Jetson Off-Device ARM64/CUDA Build Strategy (Brett/Zack, 2026-07-17)

**Summary:** Production ARM64 CUDA artifacts must be built off-device. Native x64→arm64 cross-compile preferred.


### 29. Jetson Voice Stack Implementation & Deployment (Ripley et al., 2026-07-18)

**Summary:** Off-device cross-build completed. R6 built and locally validated. K1-K5 gates OPEN.

---

**Archive created:** 2026-08-18 by Scribe
