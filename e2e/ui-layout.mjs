/** UI-only checks. All /api requests are intercepted here, never in application code.
 * Run against ng serve with npm run test:ui --prefix e2e. Real full-stack smoke: npm test --prefix e2e.
 * UI_BASE_URL, CHROME_BIN, UI_SCREENSHOTS=1 are optional. Artifacts are git-ignored.
 */
import puppeteer from "puppeteer";
import fs from "node:fs/promises";
const browser = await puppeteer.launch({
  executablePath: process.env.CHROME_BIN || undefined,
  args: ["--no-sandbox", "--disable-dev-shm-usage"],
  headless: true,
});
const base = process.env.UI_BASE_URL || "http://localhost:4200";
const output = new URL("../artifacts/ui/", import.meta.url).pathname;
let mode = "";
try {
  const page = await browser.newPage();
  const errors = [];
  const unknown = [];
  let guest = false;
  page.on("pageerror", (e) => errors.push(String(e)));
  await page.setRequestInterception(true);
  const date = "2026-10-16T14:30:00Z";
  const person = {
    id: "u1",
    userName: "nour",
    displayName: "Nour Hassan",
    hasProfilePicture: false,
    isFollowing: true,
  };
  const me = { ...person, id: "me", userName: "omar", displayName: "Omar Ali" };
  const profile = {
    ...person,
    bio: "Finding the extraordinary in ordinary places. Cairo, and everywhere in between.",
    followerCount: 128,
    followingCount: 42,
    postCount: 6,
    isCurrentUser: false,
    createdAtUtc: date,
  };
  const images = Array.from({ length: 6 }, (_, i) => ({
    id: "i" + i,
    title: [
      "The quiet between",
      "Into the blue",
      "City after hours",
      "A slower morning",
      "Layers of light",
      "Somewhere new",
    ][i],
    description:
      "A moment worth keeping. Exploring light and life in the everyday.",
    originalFileName: "photo.jpg",
    contentType: "image/jpeg",
    fileSizeBytes: 2480000,
    width: 1600,
    height: 1200,
    tags: ["travel", "light"],
    createdAtUtc: date,
    updatedAtUtc: null,
  }));
  const counts = ["like", "love", "laugh", "wow", "sad", "dislike"].map(
    (reaction) => ({ reaction, count: reaction === "love" ? 24 : 0 }),
  );
  const posts = images.map((v, i) => ({
    ...v,
    id: "p" + i,
    author: person,
    image: {
      imageId: v.id,
      contentType: "image/jpeg",
      width: 1600,
      height: 1200,
    },
    commentCount: 3,
    reactionCount: 24,
    reactionCounts: counts,
    myReaction: null,
    isMine: true,
  }));
  const messages = Array.from({ length: 30 }, (_, i) => ({
    id: "m" + i,
    conversationId: "c1",
    senderUserId: i % 2 ? "me" : "u1",
    sequence: i + 1,
    content:
      i % 2
        ? "Thank you! The light was just right that evening."
        : "I really liked your latest photograph. Where was it taken?",
    createdAtUtc: date,
    messageType: "text",
    sharedPostId: null,
  }));
  const conversation = {
    id: "c1",
    participants: [
      { ...me, lastReadSequence: 30 },
      { ...person, lastReadSequence: 29 },
    ],
    latestMessage: messages.at(-1),
    unreadCount: 2,
  };
  const prefs = {
    inAppEnabled: true,
    chatEnabled: true,
    followEnabled: true,
    commentsEnabled: true,
    reactionsEnabled: true,
    mentionsEnabled: true,
    announcementsEnabled: true,
    soundEnabled: false,
    soundVolume: 35,
    suppressActiveConversation: true,
    browserPushEnabled: false,
  };
  const notices = [
    "postReaction",
    "postComment",
    "newFollower",
    "chatMessage",
    "commentMention",
    "systemAnnouncement",
  ].map((type, i) => ({
    id: "n" + i,
    type,
    text: [
      "reacted to your image.",
      "left a comment on your post.",
      "started following you.",
      "sent you a message.",
      "mentioned you in a comment.",
      "Account security update.",
    ][i],
    actorDisplayName: i === 5 ? null : person.displayName,
    createdAtUtc: date,
    readAtUtc: i > 2 ? date : null,
    critical: i === 5,
    available: true,
    actorUserId: "u1",
    postId: "p0",
  }));
  const paged = (items) => ({
    items,
    page: 1,
    pageSize: 20,
    totalCount: items.length,
    totalPages: 1,
  });
  page.on("request", async (r) => {
    const u = new URL(r.url());
    if (!u.pathname.startsWith("/api/")) return r.continue();
    const p = u.pathname;
    let data = {};
    let status = 200;
    if (mode === "broken" && p.endsWith("/content"))
      return r.respond({ status: 404, body: "" });
    if (p === "/api/feed" && mode === "error")
      return r.respond({
        status: 500,
        contentType: "application/json",
        body: "{}",
      });
    if (p === "/api/feed" && mode === "loading")
      await new Promise((resolve) => setTimeout(resolve, 1800));
    if (p.endsWith("/content")) {
      return r.respond({
        status: 200,
        contentType: "image/svg+xml",
        body: `<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="1200"><rect width="1600" height="1200" fill="#32464c"/><circle cx="1100" cy="350" r="170" fill="#baa77a"/><path d="M0 1000L600 200 1600 1200H0" fill="#1d3034"/></svg>`,
      });
    }
    if (p === "/api/auth/me") {
      data = { id: "me", email: "omar@example.com" };
      if (guest) status = 401;
    } else if (p === "/api/feed")
      data =
        mode === "empty"
          ? paged([])
          : mode === "pagination"
            ? { ...paged(posts), totalPages: 7, totalCount: 42, pageSize: 6 }
            : paged(posts);
    else if (p === "/api/images/upload-policy")
      data = {
        maxFileSizeBytes: 10485760,
        maxFilesPerRequest: 10,
        allowedExtensions: [".jpg", ".png", ".webp"],
        allowedContentTypes: ["image/jpeg", "image/png", "image/webp"],
        titleMaxLength: 200,
        descriptionMaxLength: 2000,
        maxTagsPerImage: 20,
        tagMaxLength: 50,
      };
    else if (p === "/api/images") data = paged(images);
    else if (p.startsWith("/api/images/"))
      data = images.find((i) => p.endsWith(i.id)) ?? images[0];
    else if (p.endsWith("/comments"))
      data = paged([
        {
          id: "comment",
          postId: "p0",
          author: person,
          content: "Such a beautiful perspective. The light is incredible.",
          createdAtUtc: date,
          updatedAtUtc: null,
          isMine: false,
          canDelete: true,
        },
      ]);
    else if (p.startsWith("/api/posts/"))
      data =
        p.endsWith("reactions") || p.endsWith("my-reaction")
          ? { totalCount: 25, counts, myReaction: "love" }
          : posts[0];
    else if (
      p === "/api/users" ||
      p.endsWith("/followers") ||
      p.endsWith("/following")
    )
      data = paged([
        person,
        {
          ...person,
          id: "u2",
          displayName: "Youssef Ahmed",
          userName: "youssef",
        },
      ]);
    else if (p.endsWith("/posts")) data = paged(posts);
    else if (p.startsWith("/api/users/"))
      data =
        p === "/api/users/me"
          ? { ...profile, ...me, isCurrentUser: true }
          : profile;
    else if (p === "/api/notifications/unread-count") data = { count: 3 };
    else if (p === "/api/notifications/preferences") data = prefs;
    else if (p === "/api/notifications")
      data = paged(
        notices.filter((n) =>
          u.searchParams.get("unread") === "true" ? !n.readAtUtc : true,
        ),
      );
    else if (p === "/api/conversations") data = paged([conversation]);
    else if (p.endsWith("/messages"))
      data = u.searchParams.has("before") ? [] : messages;
    else if (p.startsWith("/api/conversations/")) data = conversation;
    else if (p.includes("/chat/")) {
      status = 503;
      data = { error: "Test harness: no SignalR backend" };
    } else if (p != "/api/auth/csrf-token") unknown.push(p);
    await r.respond({
      status,
      contentType: "application/json",
      body: JSON.stringify(data),
    });
  });
  await fs.mkdir(output, { recursive: true });
  const results = [];
  for (const width of [375, 430, 768, 1024, 1366, 1440]) {
    await page.setViewport({ width, height: 1000 });
    for (const route of [
      "/feed",
      "/images",
      "/images/i0",
      "/images/upload",
      "/posts/new",
      "/posts/p0",
      "/users?q=nour",
      "/users/u1",
      "/users/u1/followers",
      "/users/u1/following",
      "/account",
      "/notifications",
      "/chat?conversation=c1",
      "/missing",
      "/login",
      "/register",
    ]) {
      guest = route === "/login" || route === "/register";
      await page.goto(base + route, {
        waitUntil: "networkidle0",
        timeout: 30000,
      });
      const result = await page.evaluate(() => ({
        width: innerWidth,
        scroll: document.documentElement.scrollWidth,
        title: document.querySelector("h1")?.textContent,
        overflow: [...document.querySelectorAll("main *")]
          .filter(
            (e) =>
              e.getBoundingClientRect().right > innerWidth + 2 &&
              getComputedStyle(e).position !== "absolute",
          )
          .slice(0, 5)
          .map((e) => e.tagName + "." + e.className),
      }));
      results.push({ route, ...result });
      if (process.env.UI_SCREENSHOTS && [375, 1440].includes(width))
        await page.screenshot({
          path: `${output}/${width}-${route.split("?")[0].replaceAll("/", "_")}.png`,
          fullPage: true,
        });
    }
  }
  guest = false;
  const assert = (condition, message) => {
    if (!condition) throw Error(message);
  };
  const go = async (route, width = 375) => {
    await page.setViewport({ width, height: 850 });
    await page.goto(base + route, { waitUntil: "networkidle0" });
  };
  await go("/images");
  await page.click(".image-card .thumb");
  await page.waitForSelector("dialog[open] img");
  await page.waitForFunction(
    () => document.querySelector("dialog img")?.complete,
  );
  console.log(
    "viewer geometry",
    await page.evaluate(() => {
      const c = document.querySelector(".canvas");
      return {
        height: c.clientHeight,
        scroll: c.scrollHeight,
        width: c.clientWidth,
        scrollWidth: c.scrollWidth,
      };
    }),
  );
  assert(
    (await page.$eval("body", (e) => e.style.overflow)) === "hidden",
    "body scroll not locked",
  );
  await page.keyboard.press("ArrowRight");
  await page.waitForFunction(() =>
    document.querySelector(".viewer-position").textContent.includes("2 /"),
  );
  await page.waitForFunction(() => !document.querySelector(".viewer-loading"));
  await page.keyboard.press("z");
  await page.waitForSelector(".canvas.zoomed");
  await page.keyboard.press("z");
  if (process.env.UI_SCREENSHOTS)
    await page.screenshot({ path: output + "/viewer-mobile.png" });
  await page.keyboard.press("Escape");
  await page.waitForFunction(() => document.body.style.overflow === "");
  await page.waitForFunction(() => !document.querySelector("dialog[open]"));
  assert(
    (await page.$eval("body", (e) => e.style.overflow)) === "",
    "scroll lock leaked",
  );
  assert(
    await page.evaluate(() =>
      document.activeElement?.classList.contains("thumb"),
    ),
    "focus not restored to thumbnail",
  );
  await page.click(".menu-toggle");
  await page.waitForSelector(".main-nav.open");
  await page.keyboard.press("Escape");
  await page.waitForFunction(() => !document.querySelector(".main-nav.open"));
  await go("/feed");
  await page.click('[aria-label="Choose a reaction"]');
  await page.waitForSelector(".picker");
  await page.click('.picker-item[aria-label="Love"]');
  await page.waitForFunction(() => !document.querySelector(".picker"));
  await go("/notifications");
  await page.click(".filter-tabs button:last-child");
  await page.waitForFunction(
    () => document.querySelectorAll(".notification-list li").length === 3,
  );
  await go("/chat?conversation=c1");
  const geometry = await page.$eval(".history", (e) => ({
    top: e.scrollTop,
    height: e.scrollHeight,
    client: e.clientHeight,
  }));
  assert(
    geometry.height - geometry.top - geometry.client < 5,
    "history did not start at latest",
  );
  const composer = await page.$eval(
    ".composer",
    (e) => e.getBoundingClientRect().bottom,
  );
  assert(composer <= 850, "mobile composer outside viewport");
  await page.click('[aria-label="Back to conversations"]');
  await page.waitForFunction(
    () => !document.querySelector(".has-conversation"),
  );
  assert(
    (await page.$eval(
      ".conversation-panel",
      (e) => getComputedStyle(e).display,
    )) !== "none",
    "mobile inbox hidden",
  );
  await page.click(".conversation");
  await page.waitForSelector(".has-conversation");
  await go("/images/upload");
  await page.evaluate(() => {
    const data = new DataTransfer();
    data.items.add(
      new File(["bad"], "bad.exe", { type: "application/octet-stream" }),
    );
    const input = document.querySelector("#file-input");
    input.files = data.files;
    input.dispatchEvent(new Event("change", { bubbles: true }));
  });
  await page.waitForFunction(
    () =>
      document.body.textContent.includes("not supported") ||
      document.body.textContent.includes("Unsupported"),
  );

  mode = "empty";
  await go("/feed");
  await page.waitForFunction(() =>
    document.body.textContent.includes("Your feed is empty"),
  );
  mode = "error";
  await go("/feed");
  await page.waitForFunction(() =>
    document.body.textContent.includes("Couldn't load your feed"),
  );
  mode = "";
  await page.click("app-empty-state button");
  await page.waitForSelector("app-post-card");
  mode = "loading";
  await page.goto(base + "/feed", { waitUntil: "domcontentloaded" });
  await page.waitForSelector('[aria-label="Loading feed"]');
  await page.waitForSelector("app-post-card");
  mode = "pagination";
  await go("/feed");
  await page.waitForSelector(".pagination");
  assert(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
    "pagination overflow",
  );
  mode = "broken";
  await go("/images");
  await page.click(".image-card .thumb");
  await page.waitForSelector(".viewer-state");
  mode = "";
  await page.click(".viewer-state button");
  await page.waitForFunction(
    () =>
      document.querySelector("dialog img")?.complete &&
      !document.querySelector(".viewer-loading"),
  );
  await page.keyboard.press("Escape");
  const overflow = results.filter((r) => r.scroll > r.width);
  await fs.writeFile(
    output + "/results.json",
    JSON.stringify({ results, errors, unknown }, null, 2),
  );
  await browser.close();
  assert(!overflow.length, JSON.stringify(overflow));
  assert(!errors.length, JSON.stringify(errors));
  assert(!unknown.length, JSON.stringify(unknown));
  console.log(
    `UI fixtures: ${results.length} route/viewport checks and interaction/state checks passed. No real backend was used.`,
  );
} finally {
  await browser.close();
}
