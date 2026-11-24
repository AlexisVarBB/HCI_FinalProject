# AI Use & Authorship Disclosure — DetoxBet

**Team Members:** Jorge Enrique Ruiz Liera, José Carlos Béjar Gómez, Alexis Vargas Moreno  
**Declared Compliance Level:** **AI Level 1 — AI for Ideation and Feedback**

---

## 1. Summary of AI Tools Used

In strict accordance with **AI Level 1**, artificial intelligence was used exclusively for brainstorming, architectural ideation, conceptual learning, and structural feedback. No AI tools were used to generate production C# scripts, build Unity scenes, write UXML/USS layouts, or construct the Firebase database.

| AI Tool | Project Phase | Purpose (Level 1: Ideation & Feedback) | How Outputs Were Checked & Modified |
| :--- | :--- | :--- | :--- |
| **Google Gemini** | Technical Feasibility & Ideation | Brainstorming zero-cost alternatives to Apple's restricted `Screen Time API`, which requires a paid Apple Developer account and enterprise entitlements. | The team evaluated the suggested **iOS Shortcuts** concept, tested its feasibility on physical iPhones, and independently designed the event-tracking logic to match our project scope (TikTok and Instagram). |
| **Google Gemini** | Information Architecture (Figma) | Receiving structural feedback on screen hierarchy, flow simplicity, and logical grouping of UI elements before building the prototype. | We cross-checked suggestions against our user research findings, trimming unnecessary flows to keep the interface focused on six core screens (`Login`, `Menu`, `CreateRoom`, `JoinRoom`, `Room`, `Final`). |
| **Figma AI (Built-in)** | Visual Exploration | Exploring dark-mode color palette combinations for high contrast and low eye strain on mobile devices. | The team manually refined contrast ratios, semantic status colors (active/inactive states), and typography hierarchy directly in Figma. |
| **Anthropic Claude** | WebGL & Deployment Feedback | Conceptual guidance on Unity WebGL platform constraints (mobile Safari viewport scaling, browser tab suspension behavior, and static hosting considerations for Render). | The team verified each constraint through live test builds on desktop and iOS Safari, then independently implemented the responsive layout logic (`ScreenManager.cs`), custom WebGL template, and Render deployment configuration. |
| **Anthropic Claude** | iOS Shortcuts Conceptual Guide | Explaining how iOS Personal Automations operate—specifically the difference between *Is Opened* / *Is Closed* triggers, JSON payload formatting concepts, and the *Run Immediately* setting. | Because the team had no prior experience with iOS Shortcuts, we used Claude's explanations purely as a conceptual reference, then manually built, linked, and debugged all four shortcuts and automations on physical iPhones. |
| **Google Gemini** | Documentation & HCI Alignment | Reviewing documentation structure, clarity, and alignment with HCI terminology across `README.md` and `/docs` files. | Every technical claim, architecture detail, research finding, and testing observation was authored from the team's actual project work and manually verified against the live Render app and Unity repository. |

---

## 2. Chronological Breakdown of Development & AI Involvement

1. **Initial Concept & Problem Framing (No AI):** The core idea for DetoxBet—using social accountability and lighthearted forfeits instead of coercive app blockers—originated entirely from the team's own experiences and real-world behavioral analogs (such as the "phone stacking" dinner dynamic).
2. **Feasibility & Telemetry Ideation (Google Gemini):** When we encountered the paywall and entitlement barriers of Apple's native Screen Time API, we consulted Gemini to brainstorm free, accessible alternatives. This ideation led us to explore native iOS Shortcuts as a lightweight telemetry bridge.
3. **UX Architecture & Visual Prototyping (Google Gemini & Figma AI):** Prior to finalizing our Figma prototype, we used Gemini for feedback on how to organize screens and reduce cognitive load, and Figma's built-in AI to explore dark-mode palettes. All wireframes, components, and interactive prototypes were assembled manually by the team.
4. **Figma-to-Unity UI Implementation (No AI):** Translating the Figma prototype into Unity (`UI Toolkit`, UXML/USS stylesheets, `ScreenManager.cs`, and all C# screen controllers) was carried out completely without AI.
5. **Firebase Database & Backend Architecture (No AI):** Designing the Firebase Realtime Database schema, user authentication flows, room state management, and server clock synchronization was performed manually by the team.
6. **WebGL Platform & Deployment Considerations (Anthropic Claude — Feedback Only):** As we prepared the Unity project for public web access, we consulted Claude for conceptual feedback on WebGL limitations—such as how mobile Safari handles backgrounded tabs and how WebGL templates interface with browser events. All Unity Build Profile settings, template adjustments, and Render deployments were executed and tested manually by the team.
7. **iOS Shortcuts Configuration (Anthropic Claude — Conceptual Guidance):** To bridge iOS app usage with our Firebase backend, we asked Claude to explain the mechanics of iOS Personal Automations. Using that conceptual understanding, the team manually created, configured, and tested all four shortcuts (`Abrir`/`Cerrar` for TikTok and Instagram) and verified live webhook delivery.
8. **User Testing & Iterative Refinement (No AI):** All usability testing sessions, functional test cases, bug identification, and resulting improvements (session persistence, per-app time breakdown, and customizable durations) were conducted and implemented directly by the team.
9. **Repository Documentation (No AI):** All Markdown documentation files were written manually by the team without AI assistance.

---

## 3. Verification & Quality Control Process

To ensure academic integrity and technical reliability under **AI Level 1**, the team applied a consistent three-step check to every piece of AI feedback:
* **Cross-Checking Against Official Constraints:** Conceptual suggestions (e.g., WebGL Safari behavior or iOS Automation triggers) were always validated empirically on real iPhones and desktop browsers rather than assumed to be accurate.
* **Filtering & Rejection:** Suggestions that introduced unnecessary complexity—such as extra onboarding screens or overly complex backend polling—were explicitly rejected in favor of a lean, responsive design.
* **Manual Implementation:** No AI-generated code blocks or assets were pasted into the repository; all implementation decisions were written, tested, and debugged by the team.

---

## 4. Authorship & Team Understanding Statement

All members of the team (**Jorge Enrique Ruiz Liera**, **José Carlos Béjar Gómez**, and **Alexis Vargas Moreno**) confirm that:
* AI was used strictly within the boundaries of **AI Level 1 (Ideation and Feedback)**.
* All Unity scripts, UI Toolkit layouts, Figma prototypes, Firebase configurations, iOS Shortcuts, and Render deployments were authored, tested, and verified by the team.
* **Every team member fully understands and can independently explain** the system architecture, HCI design rationale, user research, usability testing results, and technical trade-offs of the submitted project.