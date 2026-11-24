# User Research & Customer Journey — DetoxBet

**Team Members:** Jorge Enrique Ruiz Liera, José Carlos Béjar Gómez, Alexis Vargas Moreno  

---

## 1. Research Methodology & Context

To uncover core user behaviors, needs, and constraints around mobile screen-time management, we combined three qualitative research methods:

1. **Auto-Ethnography:** Direct observation of our own behavioral failures when relying on native iOS Screen Time limits.
2. **Analogous Research:** Analysis of real-world social accountability rituals—specifically a workplace dinner dynamic where colleagues stack their phones in the center of the table, and the first person to pick theirs up pays the bill.
3. **Semi-Structured Peer Interviews:** Rapid interviews with 3 university students (iOS users) regarding their daily habits on TikTok and Instagram, prior attempts with screen-time apps (such as native iOS limits and **Opal**), and what motivates them to stay off their phones.

---

## 2. Key Findings, User Needs & Constraints

Following an evidence-based synthesis structure, we isolated three primary user needs and behavioral constraints that directly shaped our system architecture.

### User Need 1: Externalizing Accountability (Too Easy to Bypass)
* **Evidence:** *"When the native iOS Screen Time limit pops up, I just tap 'Ignore Limit for Today' because it's only one button away. There are no real consequences."* (Auto-ethnography & Peer Interviews).
* **Behavior / Pattern:** Self-imposed friction fails when willpower drops because the user can dismiss the barrier instantly with zero external consequences.
* **User Need:** A student trying to focus needs a way to attach external, social consequences to opening distracting apps, because internal willpower alone is not a strong enough deterrent.

### User Need 2: Autonomy Without Coercive Lockouts (The "Opal" Extreme)
* **Evidence:** A peer who used **Opal**—a strict app blocker that places a hard lock on distracting apps that is genuinely difficult to remove—reported feeling frustrated and trapped: *"I hated not being able to enter the app at all, even if I just needed to check a message or something quick. It felt too restrictive, so I stopped using it."*
* **Behavior / Pattern:** While weak limits are ignored, overly rigid lockouts trigger psychological reactance (loss of control), causing users to uninstall or abandon the tool altogether.
* **User Need:** Users need a system that lets them stay in full control of their device—never completely locking them out of their apps—while still discouraging mindless doomscrolling.

### User Need 3: Meaningful but Safe Stakes
* **Evidence:** Inspired by the corporate "Phone Stacking" dynamic (where the first person to touch their phone pays the dinner bill), participants agreed that tangible stakes drive compliance. However, interviewees noted that betting *real money* directly through an app felt "too risky" or "like gambling."
* **Behavior / Pattern:** Social stakes motivate adherence, but unregulated or high-risk stakes create anxiety and friction before a session even starts.
* **User Need:** Groups of friends need a curated set of lighthearted, pre-agreed social forfeits (e.g., *Pay for dinner*, *Buy coffee*, *Sing karaoke*) that create fun accountability without financial risk or conflict.

### Key Platform & Behavioral Constraints Identified
* **Zero Manual Logging:** Users will not manually log their distracted minutes; tracking must happen silently in the background.
* **iOS Ecosystem Restriction:** Apple restricts third-party access to the native `Screen Time API` without enterprise entitlements, requiring a lightweight OS-level workaround (iOS Shortcuts).

---

## 3. Customer Journey Map

**Persona:** "Alex", a university student who wants to stay focused with friends but impulsively opens TikTok and dislikes strict blockers like Opal.  
**Scenario:** A group of 4 friends starts a 2-hour DetoxBet challenge during a study session.

| Phase | User Action (Flow) | Thoughts & Emotions | Touchpoints | Friction & Opportunities |
| :--- | :--- | :--- | :--- | :--- |
| **1. Setup & Commitment** | **Host:** Creates room, sets 2-hour duration, selects *"Pay for dinner"* as the punishment, and shares the 4-character code.<br>**Guest (Alex):** Enters the code and joins. | *Thought:* "I can definitely last 2 hours. Let's make Carlos pay for dinner."<br>*Emotion:* Excited, competitive, in control. | DetoxBet Web App (`CreateRoom` / `JoinRoom`). | **Friction:** Long invite links or debating custom punishments slows down setup.<br>**Opportunity:** 4-character room codes and a pre-determined punishment list make onboarding fast and conflict-free. |
| **2. The Temptation** | 45 minutes in, studying gets tedious. Alex reflexively unlocks their iPhone and taps the TikTok icon. | *Thought:* "I'll just check it for a minute—at least I'm not locked out of my phone."<br>*Emotion:* Bored, impulsive. | iPhone Home Screen $\rightarrow$ TikTok App. | **Friction:** Native iOS limits are dismissed in one tap; hard blockers like Opal cause frustration.<br>**Opportunity:** Allow access (preserving autonomy) while silently capturing the event via iOS Shortcuts. |
| **3. Live Detection** | The iOS Personal Automation triggers in the background (`Run Immediately`), sending a webhook to Firebase. DetoxBet updates the shared live leaderboard. | *Thought:* "Oh no, my time just started going up on everyone's screen—I need to close this!"<br>*Emotion:* Caught, slightly panicked, amused. | iOS Shortcuts (Background) $\rightarrow$ Firebase $\rightarrow$ DetoxBet `Room` screen. | **Friction:** Delayed or manual updates break the sense of accountability.<br>**Opportunity:** Real-time polling (`1.5s`) turns visibility into an immediate social nudge rather than a rigid lockout. |
| **4. Resolution & Verdict** | The 2-hour timer ends. All devices transition to the `Final` screen. Alex has the highest distracted time. | *Thought:* "Fair enough, I lost. At least it's just dinner with my friends."<br>*Emotion:* Resigned, entertained, socially connected. | DetoxBet `Final` screen. | **Friction:** Arguing over who lost or what the penalty should be.<br>**Opportunity:** Displaying the exact server-synced times alongside the locked-in forfeit guarantees a clear, undisputed resolution. |

---

## 4. Design Traceability (How Research Drove the Architecture)

Every core decision documented in `DesignJustification.md` traces directly back to this evidence:

1. **Live Social Leaderboard Instead of Soft Limits (Need 1):** Because native iOS limits are bypassed with a single tap, DetoxBet replaces private pop-ups with a shared, real-time leaderboard (`RoomController.cs`), turning social visibility into the primary deterrent.
2. **Non-Coercive Background Tracking via iOS Shortcuts Instead of Hard Locks (Need 2 & Constraints):** Because strict blockers like **Opal** frustrate users by removing control, DetoxBet never blocks TikTok or Instagram. Instead, it uses silent iOS Personal Automations (`open`/`close` webhooks processed by `ShortcutService.cs`) to track usage automatically—keeping the user *"in control, but safe."*
3. **Curated Punishment Catalog & One-Room Lock (Need 3):** Directly inspired by the "Phone Stacking" dinner dynamic, `CreateRoomController.cs` restricts forfeits to a safe, pre-determined list (including *Pay for dinner*) while locking each participant to one active room (`current_room`) so no one can quit mid-challenge when losing.