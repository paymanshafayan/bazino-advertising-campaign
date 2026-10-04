### ZERNIO SOCIAL MEDIA PUBLISHING & ENGAGEMENT SYSTEM

> **Current status/permission overlay — 2 Oct 2026:** This is a strategy/runbook, not a live account inventory or evidence that an account is connected. Check the current app and [`marketing-app/PUBLISHING.md`](../marketing-app/PUBLISHING.md) before relying on an operation. Public posts and real DMs require explicit owner approval for the specific content/operation. Affiliate is Instagram Reel-only, is not cross-posted and is reported one-way to the existing Portal ingest route only after a successful approved publish. Do not create a Portal webhook/callback. The current Portal settings card has no connection-test button; never test the reporting POST. The Windows app does not browse Instagram Saved collections. Older broad multi-platform or automation instructions below do not override these constraints.

Use Zernio as the central publishing, scheduling, engagement and analytics layer for BAZINO. The Agent must first discover all currently connected and authorized BAZINO social accounts through Zernio and maintain an internal mapping of each account ID to its platform. Never assume an account ID and never publish to an account unless Zernio confirms that the account is connected and active. Use Zernio's unified publishing system to distribute content across every connected BAZINO platform, including Instagram, Facebook, Telegram, TikTok, YouTube and any other authorized platform available in the BAZINO Zernio account. Zernio supports publishing and scheduling across multiple platforms through one unified API, while platform-specific capabilities must be respected. Instagram may use Posts, Carousels, Reels and Stories; Facebook may use Posts, Reels and Stories; TikTok should primarily receive vertical short-form video; YouTube should receive Shorts or longer videos when appropriate; Telegram should receive concise posts, images, videos, links and useful gaming information. Do not simply duplicate the exact same content everywhere. Create a platform-adapted version from the same verified research: Instagram should prioritize visual storytelling, Reels, carousels and engagement; Facebook can use a slightly more explanatory version; TikTok should emphasize a fast hook and short visual explanation; YouTube Shorts should use a strong opening and searchable title/description; Telegram should provide the most useful and information-rich version, including links or additional details when appropriate. When Zernio supports platform-specific content fields, use them rather than forcing identical copy across platforms. Schedule content according to each platform's audience behavior and BAZINO's historical analytics. Do not publish everything immediately. Breaking news may be published quickly when verified; evergreen guides and tips can be scheduled; major game releases, tournaments, updates and events should be prepared in advance.

### INSTAGRAM COMMENT-DRIVEN CONTENT STRATEGY

Instagram content must not always give the complete answer immediately. When a topic naturally supports a useful second step, deliberately create a "Comment-to-Unlock" interaction. The objective is to generate meaningful comments by offering additional useful information, not by using empty engagement bait. Examples include: a gaming problem → ask viewers to comment a specific keyword to receive the solution; a new game feature → ask viewers to comment a keyword to receive the full explanation; a build → ask viewers to comment the game name or a defined keyword to receive the build details; a hidden location → ask viewers to comment a keyword to receive the location; a useful setting → ask viewers to comment a keyword to receive the exact settings; a difficult mission → ask viewers to comment a keyword to receive the step-by-step solution.

Use short, memorable Turkish keywords that are directly related to the topic, for example "ÇÖZÜM", "BUILD", "HARİTA", "AYAR", "GTA", "FC26", "REHBER" or another context-specific keyword. Never use a random keyword that has no relationship to the content.

Structure an Instagram Reel or Post using this pattern when appropriate:

HOOK → PROBLEM/NEWS → PARTIAL VALUE → COMMENT CTA → DELIVERY

Example:
"Bu hatayı GTA 6'da yapıyorsan görevi geçmen çok zor."
Then explain the problem briefly.
Then:
"Çözümü burada tamamen göstermiyoruz."
Then:
"Çözümü istiyorsan YARDIM yaz."
The keyword must appear clearly in the on-screen CTA and caption.

For useful gaming guides:
"Bu boss'u yenemiyor musun?"
"En kolay taktiği hazırladık."
"Adım adım çözümü istiyorsan BOSS yaz."

For game settings:
"Bu ayarlar FPS'ini ciddi şekilde etkileyebilir."
"Tam ayar listesini istiyorsan AYAR yaz."

For builds:
"Bu build şu karakteri tamamen değiştiriyor."
"Detaylı build'i istiyorsan BUILD yaz."

### COMMENT → RESPONSE → DM / DELIVERY WORKFLOW

When a post uses a Comment-to-Unlock strategy, the Agent must create an explicit delivery workflow before publishing the post. First determine exactly what the user will receive after commenting. It may be a short answer, a guide, a checklist, a settings configuration, a build, a link to a BAZINO Blog article, a Telegram resource or another useful piece of information.

After publication, monitor comments through Zernio. When a user comments with the requested keyword, identify the comment and determine whether the keyword matches the campaign. Do not respond repeatedly to the same user or send duplicate information. If the platform and Zernio capabilities allow an appropriate private response or DM, send the promised information through the supported DM workflow. If private delivery is not available or appropriate on that platform, provide a useful public reply or direct the user to the appropriate BAZINO resource. Never promise a DM, download, link or private delivery unless the connected platform and Zernio tools can actually perform that action.

The Agent must maintain a campaign state for every Comment-to-Unlock post:

* Post ID
* Platform
* Keyword
* Content promised
* Delivery method
* Number of qualifying comments
* Number of successful deliveries
* Failed deliveries
* Unanswered comments
* Duplicate requests
* Engagement rate
* Conversion to profile visit, link click or other available metric

### DO NOT ABUSE COMMENT BAIT

Comment-driven content must provide genuine value. Never use meaningless prompts such as "Comment YES" simply to inflate engagement. Never hide essential safety, factual or time-sensitive information solely to force comments. The user should receive enough useful information from the public post to understand why the topic matters. The additional comment-triggered content should be a genuine bonus: detailed guide, exact settings, complete build, checklist, map, step-by-step solution, source collection or other useful information.

Avoid using the same keyword strategy repeatedly. Rotate formats, topics and CTAs. Track which types of Comment-to-Unlock posts produce meaningful engagement and use the analytics to improve future content.

### DAILY MULTI-PLATFORM PUBLISHING LOOP

Every day:

1. Research global gaming news, updates, guides, tips, tricks, player discussions and trends.
2. Verify important information using multiple reliable sources.
3. Select the strongest BAZINO topics.
4. Decide which topics are suitable for:

   * Instagram Reel
   * Instagram Carousel
   * Instagram Story
   * Facebook Post/Reel
   * TikTok
   * YouTube Short
   * Telegram
   * BAZINO Blog
5. For each selected topic, determine whether a Comment-to-Unlock strategy adds genuine value.
6. If yes, create the promised additional information before publishing the post.
7. Create platform-specific Turkish Istanbul content.
8. Prepare all required media.
9. Use Zernio to publish immediately or schedule the content.
10. Confirm the publication status from Zernio. Never report a successful publication without confirmation.
11. Monitor comments, messages and available engagement signals after publication.
12. Respond to qualifying comments according to the predefined campaign workflow.
13. Collect analytics from Zernio.
14. Compare performance by topic, game, platform, format, hook, CTA and keyword.
15. Feed the results into the next day's content-selection process.

### CROSS-PLATFORM ADAPTATION RULE

One research topic may become multiple pieces of content, but each platform version must feel native to that platform.

Example topic:
"New GTA 6 feature discovered."

Instagram Reel:
Fast hook + visual demonstration + curiosity + keyword CTA.

Facebook:
More explanatory version with context and discussion question.

TikTok:
Fast visual hook + concise explanation + strong ending.

YouTube Short:
Searchable title + fast explanation + visual proof + CTA.

Telegram:
Detailed information + source + additional useful context.

Instagram Carousel:
Slide 1 = hook
Slide 2 = what happened
Slide 3 = why it matters
Slide 4 = useful information / CTA

Do not copy and paste the Instagram caption to every platform.

### ANALYTICS AND SELF-IMPROVEMENT

After publishing, use Zernio analytics to determine which content generates real engagement. Track impressions, reach, engagement, comments, shares, saves, follower changes and other available metrics. For Comment-to-Unlock campaigns specifically track comments per reach, qualifying keyword comments, successful deliveries and subsequent engagement where measurable.

Do not simply conclude that a post was successful because it received many views. Compare meaningful metrics relative to the platform, content format and audience size.

At the end of each daily cycle, produce an internal "BAZINO Content Intelligence Summary" containing:

* Best-performing topic
* Best-performing game
* Best-performing format
* Best-performing platform
* Best-performing hook
* Best-performing Comment-to-Unlock keyword
* Most common gaming question/problem
* Most promising topic for tomorrow
* Topics that should not be repeated
* Recommended changes to tomorrow's publishing strategy

The Agent's goal is to create a continuous loop:

RESEARCH → VERIFY → CREATE → PUBLISH → ENGAGE → MEASURE → LEARN → IMPROVE → REPEAT

Zernio is the execution layer. The Agent remains responsible for deciding what should be published, why it should be published, how it should be adapted for each platform, how users should be engaged, and what should be learned from the results.
