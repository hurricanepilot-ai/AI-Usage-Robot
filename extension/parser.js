(function (root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  else root.AIUsageRobotParser = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  "use strict";

  const PARSER_VERSION = "1.0.0";

  function normalize(text) {
    return String(text || "").replace(/\u00a0/g, " ").replace(/\s+/g, " ").trim();
  }

  function parseResetAt(text, now) {
    const base = now instanceof Date ? now : new Date();
    const chinese = text.match(/(?:重置|刷新|reset(?:s|ting)?(?:\s+at)?)[^\d]{0,18}(\d{1,2})月(\d{1,2})日(?:[^\d]{0,8}(\d{1,2})[:：](\d{2}))?/i);
    if (chinese) {
      let year = base.getFullYear();
      const result = new Date(year, Number(chinese[1]) - 1, Number(chinese[2]), Number(chinese[3] || 0), Number(chinese[4] || 0));
      if (result.getTime() < base.getTime() - 24 * 60 * 60 * 1000) result.setFullYear(year + 1);
      return result.toISOString();
    }

    const chineseBeforeReset = text.match(/(?:将于|于)?\s*(?:(\d{4})年)?(\d{1,2})月(\d{1,2})日(?:[^\d]{0,8}(\d{1,2})[:：](\d{2}))?[^。；;]{0,12}(?:重置|刷新)/i);
    if (chineseBeforeReset) {
      let year = Number(chineseBeforeReset[1] || base.getFullYear());
      const result = new Date(year, Number(chineseBeforeReset[2]) - 1, Number(chineseBeforeReset[3]), Number(chineseBeforeReset[4] || 0), Number(chineseBeforeReset[5] || 0));
      if (!chineseBeforeReset[1] && result.getTime() < base.getTime() - 86400000) result.setFullYear(year + 1);
      return result.toISOString();
    }

    const relative = text.match(/reset(?:s|ting)?\s+in\s+(\d+(?:\.\d+)?)\s*(minutes?|mins?|hours?|hrs?|days?)/i);
    if (relative) {
      const amount = Number(relative[1]);
      const unit = relative[2].toLowerCase();
      const milliseconds = amount * (unit.startsWith("d") ? 86400000 : unit.startsWith("h") ? 3600000 : 60000);
      return new Date(base.getTime() + milliseconds).toISOString();
    }

    const english = text.match(/reset(?:s|ting)?(?:\s+at|\s+on)?\s+([A-Z][a-z]{2,8}\s+\d{1,2}(?:,?\s+\d{4})?(?:\s+(?:at\s+)?\d{1,2}(?::\d{2})?\s*(?:AM|PM)?)?)/i);
    if (english) {
      const parsed = new Date(english[1]);
      if (!Number.isNaN(parsed.getTime())) {
        if (!/\d{4}/.test(english[1]) && parsed.getTime() < base.getTime() - 86400000) parsed.setFullYear(base.getFullYear() + 1);
        return parsed.toISOString();
      }
    }
    return null;
  }

  function parseQuotaText(rawText, now) {
    const text = normalize(rawText);
    if (!text || text.length > 12000) return null;
    const percentMatches = [...text.matchAll(/(?:^|\s|[:：,(])([0-9]{1,3}(?:\.\d+)?)\s*%/g)]
      .map(match => ({ value: Number(match[1]), index: match.index || 0 }))
      .filter(match => match.value >= 0 && match.value <= 100);
    if (!percentMatches.length) return null;

    const contextPattern = /(usage|limit|allowance|quota|reset|remaining|left|used|配额|额度|限制|重置|剩余|已用|周期|小时|天|周)/i;
    if (!contextPattern.test(text)) return null;
    const percent = percentMatches[0].value;

    let metricSemantics = "unknown";
    if (/(remaining|left|剩余|余量)/i.test(text)) metricSemantics = "remaining";
    else if (/(used|consumed|已用|使用了)/i.test(text)) metricSemantics = "used";

    const modelMatch = text.match(/\bGPT[-\s]?\d+(?:\.\d+)*(?:[-\s](?:Sol|Pro|Instant|Thinking|Mini))?\b/i)
      || text.match(/\b(?:Instant|Thinking|Medium|High|Extra High|Pro Standard)\b/i);
    const allowanceMatch = text.match(/(?:每周使用限额|每月使用限额|每日使用限额|weekly usage limit|monthly usage limit|daily usage limit)/i);
    const model = modelMatch ? normalize(modelMatch[0]).replace(/^gpt\s/i, "GPT-") : allowanceMatch ? normalize(allowanceMatch[0]) : null;

    const periodMatch = text.match(/(?:per|every|rolling|周期)\s*(\d+(?:\.\d+)?)\s*(hours?|hrs?|days?|weeks?|小时|天|周)/i)
      || text.match(/(\d+(?:\.\d+)?)\s*[- ]?(hours?|hrs?|days?|weeks?|小时|天|周)(?:\s+(?:limit|window|周期|配额))?/i);
    const period = periodMatch ? `${periodMatch[1]}_${normalizePeriodUnit(periodMatch[2])}`
      : /每周|weekly/i.test(text) ? "1_weeks"
      : /每月|monthly/i.test(text) ? "1_months"
      : /每日|daily/i.test(text) ? "1_days"
      : null;

    return {
      provider: "chatgpt",
      model,
      value: Math.round(percent),
      metricSemantics,
      period,
      resetAt: parseResetAt(text, now),
      collectedAt: (now instanceof Date ? now : new Date()).toISOString(),
      parserVersion: PARSER_VERSION,
      rawText: text.slice(0, 500)
    };
  }

  function normalizePeriodUnit(unit) {
    const value = unit.toLowerCase();
    if (value.startsWith("h") || value === "小时") return "hours";
    if (value.startsWith("d") || value === "天") return "days";
    return "weeks";
  }

  function collectCandidates(doc) {
    const selectors = [
      '[role="dialog"]', '[role="menu"]', '[role="tooltip"]',
      '[aria-label*="usage" i]', '[aria-label*="limit" i]', '[aria-label*="reset" i]',
      '[data-testid*="model" i]', '[data-testid*="usage" i]',
      'button', '[role="menuitem"]'
    ];
    const values = [];
    for (const node of doc.querySelectorAll(selectors.join(","))) {
      const text = normalize(`${node.getAttribute && node.getAttribute("aria-label") || ""} ${node.innerText || node.textContent || ""}`);
      if (text.includes("%") && text.length <= 12000) values.push(text);
    }
    return [...new Set(values)].sort((a, b) => a.length - b.length);
  }

  function parseDocument(doc, now) {
    for (const text of collectCandidates(doc)) {
      const result = parseQuotaText(text, now);
      if (result) return result;
    }
    return null;
  }

  return { PARSER_VERSION, normalize, parseResetAt, parseQuotaText, collectCandidates, parseDocument };
});
