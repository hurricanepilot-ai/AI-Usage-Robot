const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const parser = require("../parser.js");

test("parses Chinese quota fixture without fixed DOM paths", () => {
  const html = fs.readFileSync(path.join(__dirname, "fixtures", "quota-zh.html"), "utf8");
  const visibleText = html.replace(/<[^>]+>/g, " ");
  const result = parser.parseQuotaText(visibleText, new Date("2026-08-13T08:18:00Z"));
  assert.equal(result.value, 98);
  assert.equal(result.metricSemantics, "remaining");
  assert.equal(result.model, "GPT-5.6 Sol");
  assert.equal(result.period, "1_weeks");
  const reset = new Date(result.resetAt);
  assert.equal(reset.getMonth(), 7);
  assert.equal(reset.getDate(), 20);
  assert.equal(reset.getHours(), 10);
});

test("keeps percentage semantics unknown when text does not say used or remaining", () => {
  const result = parser.parseQuotaText("GPT-5.6 Pro usage limit 64% every 3 hours", new Date());
  assert.equal(result.value, 64);
  assert.equal(result.metricSemantics, "unknown");
  assert.equal(result.period, "3_hours");
});

test("rejects unrelated percentages", () => {
  assert.equal(parser.parseQuotaText("Download complete 98%", new Date()), null);
});

test("parses the current ChatGPT Chinese weekly usage panel", () => {
  const result = parser.parseQuotaText(
    "使用限额 跟踪套餐限额内的使用情况 每周使用限额 剩余 94% 将于 2026年8月20日 14:07 重置",
    new Date("2026-08-13T09:10:00Z"));
  assert.equal(result.value, 94);
  assert.equal(result.metricSemantics, "remaining");
  assert.equal(result.model, "每周使用限额");
  assert.equal(result.period, "1_weeks");
  const reset = new Date(result.resetAt);
  assert.equal(reset.getFullYear(), 2026);
  assert.equal(reset.getMonth(), 7);
  assert.equal(reset.getDate(), 20);
  assert.equal(reset.getHours(), 14);
  assert.equal(reset.getMinutes(), 7);
});
