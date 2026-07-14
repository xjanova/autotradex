/*
 * ============================================================================
 * AutoTrade-X - News Sentiment Analyzer
 * ============================================================================
 * Pure lexicon-based sentiment scoring for crypto news headlines.
 * No network access — testable in isolation. The Infrastructure layer
 * fetches headlines and calls into this class.
 * ============================================================================
 */

using AutoTradeX.Core.Models;

namespace AutoTradeX.Core.Services;

/// <summary>
/// วิเคราะห์ Sentiment ของหัวข้อข่าวคริปโตด้วย lexicon ถ่วงน้ำหนัก
/// คะแนนต่อข่าว -100 ถึง +100
/// </summary>
public static class SentimentAnalyzer
{
    // Weighted lexicon: phrase → score. Multi-word phrases are matched first.
    // น้ำหนักตามความรุนแรงของผลกระทบต่อราคา
    private static readonly Dictionary<string, int> Lexicon = new(StringComparer.OrdinalIgnoreCase)
    {
        // ===== Strong bullish (+30 to +40) =====
        ["all-time high"] = 40, ["all time high"] = 40, ["record high"] = 35,
        ["etf approval"] = 40, ["etf approved"] = 40, ["spot etf"] = 25,
        ["etf inflow"] = 30, ["institutional adoption"] = 35,
        ["halving"] = 20, ["supply shock"] = 30,
        ["surges"] = 30, ["surge"] = 30, ["soars"] = 35, ["soar"] = 35,
        ["skyrockets"] = 35, ["skyrocket"] = 35, ["rallies"] = 30, ["rally"] = 30,
        ["breakout"] = 25, ["breaks out"] = 25, ["parabolic"] = 30,
        ["strategic reserve"] = 30, ["legal tender"] = 30,

        // ===== Moderate bullish (+10 to +25) =====
        ["bullish"] = 20, ["bull run"] = 25, ["bull market"] = 20,
        ["adoption"] = 15, ["partnership"] = 15, ["integration"] = 10,
        ["upgrade"] = 15, ["mainnet"] = 10, ["listing"] = 15, ["listed on"] = 15,
        ["accumulation"] = 15, ["accumulate"] = 10, ["whale buys"] = 20,
        ["buys the dip"] = 15, ["buying the dip"] = 15,
        ["gains"] = 15, ["climbs"] = 15, ["jumps"] = 20, ["rebounds"] = 15,
        ["recovers"] = 15, ["recovery"] = 10, ["outperforms"] = 15,
        ["inflows"] = 15, ["funding round"] = 10, ["investment"] = 10,
        ["approval"] = 15, ["approves"] = 15, ["green light"] = 15,
        ["burns"] = 10, ["token burn"] = 15, ["buyback"] = 15,
        ["upgrade complete"] = 15, ["staking rewards"] = 5,

        // ===== Strong bearish (-30 to -45) =====
        ["hacked"] = -40, ["hack"] = -35, ["exploit"] = -35, ["exploited"] = -40,
        ["stolen"] = -35, ["security breach"] = -40, ["rug pull"] = -45,
        ["bankruptcy"] = -40, ["bankrupt"] = -40, ["insolvency"] = -40, ["insolvent"] = -40,
        ["collapse"] = -40, ["collapses"] = -40, ["crashes"] = -35, ["crash"] = -35,
        ["plunges"] = -30, ["plunge"] = -30, ["plummets"] = -35, ["plummet"] = -35,
        ["fraud"] = -35, ["ponzi"] = -40, ["scam"] = -35,
        ["banned"] = -35, ["ban"] = -30, ["crackdown"] = -30,
        ["sec sues"] = -35, ["lawsuit"] = -25, ["sued"] = -25, ["indicted"] = -30,
        ["liquidations"] = -25, ["liquidation"] = -25, ["liquidated"] = -25,
        ["death cross"] = -25, ["capitulation"] = -30,
        ["delisting"] = -30, ["delisted"] = -30, ["delist"] = -30,
        ["exit scam"] = -45, ["halts withdrawals"] = -40, ["freezes withdrawals"] = -40,

        // ===== Moderate bearish (-10 to -25) =====
        ["bearish"] = -20, ["bear market"] = -20, ["selloff"] = -25, ["sell-off"] = -25,
        ["dumps"] = -25, ["dump"] = -20, ["tumbles"] = -25, ["tumble"] = -25,
        ["slides"] = -15, ["slumps"] = -20, ["slump"] = -20, ["sinks"] = -20,
        ["drops"] = -15, ["drop"] = -10, ["falls"] = -15, ["fall"] = -10,
        ["declines"] = -15, ["decline"] = -10, ["dips"] = -10,
        ["outflows"] = -15, ["etf outflow"] = -25,
        ["whale sells"] = -20, ["whale dumps"] = -25, ["miners sell"] = -15,
        ["regulation"] = -10, ["regulatory"] = -10, ["investigation"] = -20,
        ["warning"] = -15, ["warns"] = -15, ["fears"] = -15, ["fear"] = -10,
        ["fine"] = -15, ["fined"] = -15, ["penalty"] = -15,
        ["downgrade"] = -15, ["rejected"] = -20, ["rejects"] = -20, ["delay"] = -10,
        ["under pressure"] = -15, ["uncertainty"] = -10, ["volatile"] = -5,
        ["correction"] = -15, ["profit-taking"] = -10, ["shorts"] = -10,
    };

    // Symbol → common names used in headlines (lowercase). ใช้กรองข่าวเฉพาะเหรียญ
    private static readonly Dictionary<string, string[]> CoinAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC"] = new[] { "bitcoin", "btc" },
        ["ETH"] = new[] { "ethereum", "ether", "eth" },
        ["BNB"] = new[] { "bnb", "binance coin" },
        ["SOL"] = new[] { "solana", "sol" },
        ["XRP"] = new[] { "xrp", "ripple" },
        ["ADA"] = new[] { "cardano", "ada" },
        ["DOGE"] = new[] { "dogecoin", "doge" },
        ["DOT"] = new[] { "polkadot", "dot" },
        ["MATIC"] = new[] { "polygon", "matic" },
        ["POL"] = new[] { "polygon", "pol" },
        ["AVAX"] = new[] { "avalanche", "avax" },
        ["LINK"] = new[] { "chainlink", "link" },
        ["UNI"] = new[] { "uniswap", "uni" },
        ["LTC"] = new[] { "litecoin", "ltc" },
        ["ATOM"] = new[] { "cosmos", "atom" },
        ["NEAR"] = new[] { "near protocol", "near" },
        ["APT"] = new[] { "aptos", "apt" },
        ["ARB"] = new[] { "arbitrum", "arb" },
        ["OP"] = new[] { "optimism" },
        ["SHIB"] = new[] { "shiba inu", "shib" },
        ["PEPE"] = new[] { "pepe" },
        ["SUI"] = new[] { "sui" },
        ["TON"] = new[] { "toncoin", "ton" },
        ["TRX"] = new[] { "tron", "trx" },
        ["XLM"] = new[] { "stellar", "xlm" },
        ["FIL"] = new[] { "filecoin", "fil" },
        ["ICP"] = new[] { "internet computer", "icp" },
        ["INJ"] = new[] { "injective", "inj" },
        ["KUB"] = new[] { "bitkub coin", "kub" },
    };

    /// <summary>
    /// ให้คะแนน Sentiment ของหัวข้อข่าว 1 ข่าว (-100 ถึง +100)
    /// พร้อมรายการคำที่ตรวจพบ
    /// </summary>
    public static (int score, List<string> matched) ScoreHeadline(string headline)
    {
        if (string.IsNullOrWhiteSpace(headline)) return (0, new List<string>());

        var text = " " + headline.ToLowerInvariant() + " ";
        var matched = new List<string>();
        var total = 0;

        // Match longer phrases first so "etf outflow" doesn't double-count "outflows"
        foreach (var entry in Lexicon.OrderByDescending(e => e.Key.Length))
        {
            var idx = text.IndexOf(entry.Key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            // Require word boundaries to avoid "ban" matching inside "bank"
            var before = idx == 0 ? ' ' : text[idx - 1];
            var afterIdx = idx + entry.Key.Length;
            var after = afterIdx >= text.Length ? ' ' : text[afterIdx];
            if (char.IsLetterOrDigit(before) || char.IsLetterOrDigit(after)) continue;

            total += entry.Value;
            matched.Add(entry.Key);
            // Blank out the matched region to prevent overlapping shorter matches
            text = text.Remove(idx, entry.Key.Length).Insert(idx, new string('#', entry.Key.Length));
        }

        return (Math.Clamp(total, -100, 100), matched);
    }

    /// <summary>
    /// ตรวจว่าหัวข้อข่าวพูดถึงเหรียญนี้โดยตรงหรือไม่
    /// </summary>
    public static bool IsAboutCoin(string headline, string baseAsset)
    {
        if (string.IsNullOrWhiteSpace(headline) || string.IsNullOrWhiteSpace(baseAsset))
            return false;

        var aliases = CoinAliases.TryGetValue(baseAsset, out var known)
            ? known
            : new[] { baseAsset.ToLowerInvariant() };

        var text = headline.ToLowerInvariant();
        foreach (var alias in aliases)
        {
            var idx = text.IndexOf(alias, StringComparison.Ordinal);
            while (idx >= 0)
            {
                var before = idx == 0 ? ' ' : text[idx - 1];
                var afterIdx = idx + alias.Length;
                var after = afterIdx >= text.Length ? ' ' : text[afterIdx];
                if (!char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after))
                    return true;
                idx = text.IndexOf(alias, idx + 1, StringComparison.Ordinal);
            }
        }
        return false;
    }

    /// <summary>
    /// สรุป Sentiment จากรายการข่าว — ถ่วงน้ำหนักข่าวใหม่มากกว่าข่าวเก่า
    /// (ข่าวอายุ 0-6 ชม. น้ำหนักเต็ม, ลดหลั่นจนเหลือ 25% ที่ 48 ชม.)
    /// </summary>
    public static NewsSentimentSummary Summarize(string baseAsset, IEnumerable<NewsArticle> articles, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var summary = new NewsSentimentSummary { BaseAsset = baseAsset, FetchedAt = now };

        decimal marketWeighted = 0, marketWeightSum = 0;
        decimal coinWeighted = 0, coinWeightSum = 0;

        foreach (var article in articles.OrderByDescending(a => a.PublishedAt))
        {
            summary.Articles.Add(article);

            if (article.SentimentScore > 5) summary.BullishCount++;
            else if (article.SentimentScore < -5) summary.BearishCount++;
            else summary.NeutralCount++;

            var ageHours = Math.Max(0, (now - article.PublishedAt).TotalHours);
            // Recency weight: 1.0 within 6h → 0.25 at 48h+
            var weight = (decimal)Math.Max(0.25, 1.0 - (ageHours - 6) / 56.0);
            if (ageHours <= 6) weight = 1.0m;

            marketWeighted += article.SentimentScore * weight;
            marketWeightSum += weight;

            if (article.IsCoinSpecific)
            {
                coinWeighted += article.SentimentScore * weight;
                coinWeightSum += weight;
            }
        }

        summary.MarketScore = marketWeightSum > 0
            ? Math.Clamp((int)(marketWeighted / marketWeightSum), -100, 100)
            : 0;
        summary.CoinScore = coinWeightSum > 0
            ? Math.Clamp((int)(coinWeighted / coinWeightSum), -100, 100)
            : null;

        return summary;
    }
}
