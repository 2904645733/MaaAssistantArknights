#include "TutorialProgressTaskPlugin.h"

#include <algorithm>
#include <cctype>
#include <charconv>
#include <cmath>
#include <string_view>

#include <opencv2/imgproc.hpp>

#include "Config/TaskData.h"
#include "Controller/Controller.h"
#include "Task/ProcessTask.h"
#include "Utils/Logger.hpp"
#include "Vision/Matcher.h"
#include "Vision/OCRer.h"

using namespace asst;

namespace
{
// 动作写法：C:x,y / G:x1,y1,x2,y2,ms / W:ms
constexpr char PointSeparator = ',';

std::vector<std::string> split(std::string_view text, char sep)
{
    std::vector<std::string> out;
    size_t begin = 0;
    while (true) {
        const size_t pos = text.find(sep, begin);
        if (pos == std::string_view::npos) {
            out.emplace_back(text.substr(begin));
            break;
        }
        out.emplace_back(text.substr(begin, pos - begin));
        begin = pos + 1;
    }
    return out;
}

std::optional<int> to_int(const std::string& text)
{
    if (text.empty()) {
        return std::nullopt;
    }
    int value = 0;
    const auto [end, error] = std::from_chars(text.data(), text.data() + text.size(), value);
    if (error != std::errc {} || end != text.data() + text.size()) {
        return std::nullopt;
    }
    return value;
}

// 规则表一条：<段名>|<句子>|<动作1> <动作2> ...
// 注意 Rule 是插件的私有嵌套类型，匿名命名空间里看不到，所以这里用一个本地的
// 小结构中转，再由 load_rules 组装成 Rule。
struct RawRule
{
    std::string segment;
    std::string text;
    std::vector<std::string> actions;
};

RawRule split_rule(const std::string& raw)
{
    RawRule rule;

    const std::vector<std::string> parts = split(raw, '|');
    if (parts.size() >= 1) {
        rule.segment = parts[0];
    }
    if (parts.size() >= 2) {
        rule.text = parts[1];
    }
    if (parts.size() >= 3) {
        for (const std::string& token : split(parts[2], ' ')) {
            if (!token.empty()) {
                rule.actions.emplace_back(token);
            }
        }
    }
    return rule;
}

std::string strip_space(std::string text)
{
    std::erase_if(text, [](unsigned char ch) { return std::isspace(ch) != 0; });
    return text;
}

// OCR 认出来的标点常和原文不一样（实测：原文"博士，光靠之前…"被认成"博士,光靠之前…"）。
// 查表前把两边都归一，否则永远匹配不上。
std::string normalize_punctuation(std::string text)
{
    static const std::vector<std::pair<std::string, std::string>> Replacements = {
        { "。", "，" }, { "、", "，" }, { "；", "，" }, { "：", "，" }, { ",", "，" }, { ".", "，" },  { ";", "，" },
        { ":", "，" },  { "．", "，" }, { "！", "？" }, { "!", "？" },  { "?", "？" }, { "～", "－" }, { "~", "－" },
    };
    for (const auto& [from, to] : Replacements) {
        for (size_t pos = text.find(from); pos != std::string::npos; pos = text.find(from, pos + to.size())) {
            text.replace(pos, from.size(), to);
        }
    }
    return text;
}

std::string normalize_for_match(std::string text)
{
    return normalize_punctuation(strip_space(std::move(text)));
}

// 两个字符串的相似度 = 2 * 最长公共子序列长度 / (两边长度之和)，范围 0..1。
//
// ★ 为什么不要求"指纹在原文里连续出现"：中文 OCR 认错一两个字是常态，严格要求一模一样
//   会让整条规则失效。实机证据（同一次运行里）：
//     表里 点击此处以招募干员        OCR 点击此处以招募干员          "招"->"招"，连续匹配失败
//     表里 …可能还不足以面对之后…    OCR …可能还不足以面对之后…      命中
//     表里 …可能还不足以面对之后…    OCR …可能还不足以面之后…        漏掉"对"字，连续匹配失败
//   同一句话 OCR 时好时坏，于是表现成"有时能点、有时不点"。
//   改成相似度后这三例分别得 0.947 / 1.000 / 0.968；无关文字（含被拼接进来的 UI 文字）
//   都低于 0.67。离线验证见 新手教程\工具\验证相似度匹配.py。
//
//   ★ 单位是【字符】，不是字节（下面 common_length_chars 有详细说明为什么）。
/* UTF-8 字符切分：返回每个字符（码点）的起始字节下标，末尾追加 size() 作为哨兵。
   例："abc" -> {0, 1, 2, 3}。

   ★ 为什么必须按字符切、不能按字节比：
     汉字是 3 字节，而 UTF-8 的首字节只有 E4/E5/E6/E7 这几种。不同汉字经常共享首字节，
     例如「按」= E6 8C 89、「中」= E4 B8 AD、「击」= E5 87 BB、「白」= E7 99 BD。
     按字节做 LCS 会把这些"半个汉字"当成匹配 —— 实测「点击选中干员」(18 字节) 和
     「点击首页按钮回到中央大厅」凭空公共出 13 字节（13 不是 3 的倍数，一眼就是错的），
     13/18 = 72% 越过 70% 线被判满分，于是插件去点了「点击选中干员」而不是正确的
     「点击首页按钮回到中央大厅」。按字符比就没这回事。 */
std::vector<size_t> utf8_char_offsets(const std::string& text)
{
    std::vector<size_t> offsets;
    offsets.reserve(text.size() / 3 + 1);
    size_t i = 0;
    while (i < text.size()) {
        offsets.push_back(i);
        const unsigned char byte = static_cast<unsigned char>(text[i]);
        size_t step = 1;
        if (byte >= 0xF0) {
            step = 4;
        }
        else if (byte >= 0xE0) {
            step = 3;
        }
        else if (byte >= 0xC0) {
            step = 2;
        }
        i += step;
    }
    offsets.push_back(text.size()); // 哨兵：最后一个字符的结束位置
    return offsets;
}

/* 最长公共子序列长度，单位是【字符】。
   lhs/rhs 是上一函数算出的字符边界表，比较时逐字符比（比 3 字节整块，不是逐字节）。 */
size_t common_length_chars(
    const std::string& lhs,
    const std::vector<size_t>& lhs_offsets,
    const std::string& rhs,
    const std::vector<size_t>& rhs_offsets)
{
    const size_t lhs_chars = lhs_offsets.empty() ? 0 : lhs_offsets.size() - 1;
    const size_t rhs_chars = rhs_offsets.empty() ? 0 : rhs_offsets.size() - 1;
    if (lhs_chars == 0 || rhs_chars == 0) {
        return 0;
    }
    // 超长串走便宜路径（本插件里不会出现，纯保险）
    if (lhs_chars * rhs_chars > 65'536) {
        return std::min(lhs_chars, rhs_chars);
    }

    const auto same_char = [&](size_t i, size_t j) {
        const size_t ls = lhs_offsets[i];
        const size_t le = lhs_offsets[i + 1];
        const size_t rs = rhs_offsets[j];
        const size_t re = rhs_offsets[j + 1];
        return (le - ls) == (re - rs) && lhs.compare(ls, le - ls, rhs, rs, re - rs) == 0;
    };

    std::vector<size_t> prev(rhs_chars + 1, 0);
    std::vector<size_t> cur(rhs_chars + 1, 0);
    for (size_t i = 1; i <= lhs_chars; ++i) {
        for (size_t j = 1; j <= rhs_chars; ++j) {
            cur[j] = same_char(i - 1, j - 1) ? prev[j - 1] + 1 : std::max(prev[j], cur[j - 1]);
        }
        std::swap(prev, cur);
        std::fill(cur.begin(), cur.end(), 0);
    }
    return prev[rhs_chars];
}

/* 说明：这里原来有个 offset_by(matched, move)，用来套官方的 rectMove 偏移。
   已删除 —— 官方的 rectMove [125,20,200,60] 是给它自己的"战斗中头像对话框"用的，
   位置和招募/编队屏的对话框不一样，套过来会点到屏幕左上角 (303,86)。
   现在的做法：直接点官方模板匹配到的那个框的中心。 */

} // namespace

bool asst::TutorialProgressTaskPlugin::verify(AsstMsg msg, const json::value& details) const
{
    if (details.get("subtask", std::string()) != "ProcessTask") {
        return false;
    }
    // 哨兵任务排在轮询列表第一位、每轮必然命中，是本插件唯一的唤醒点
    if (details.get("details", "task", "") != ScanTaskName) {
        return false;
    }
    return msg == AsstMsg::SubTaskStart;
}

bool asst::TutorialProgressTaskPlugin::_run()
{
    LogTraceFunction;

    if (!load_rules()) {
        return true;
    }

    const cv::Mat image = ctrler()->get_image();
    if (image.empty()) {
        LogError << __FUNCTION__ << "| empty screenshot";
        return true;
    }

    // ⓪ 剧情跳过：先看右上角有没有「跳过 ▶」。
    //   教程里有三段纯剧情演出（起名后的开场、序章·上结束、序章·下结束），
    //   那几屏【没有对话框、也没有 BattleAvatarDialog 标志】—— 只看头像标志的话
    //   插件会干等着、教程卡死。所以把"有跳过按钮"当成"在剧情里"的判据：
    //   先点跳过 + 确认，本轮就结束，下一轮再回到正常的头像识别。
    //   （用户要求：先跳过剧情并确认，再进行头像标志识别。）
    if (try_skip_plot()) {
        return true;
    }

    // ① 判断"在不在对话"：用【官方 BattleAvatarDialog.png】在官方 roi 里匹配。
    //   模板内容是屏幕左上角的 RHODES ISLAND 标志 / 会话招牌（不是头像 —— 我一开始
    //   就搞错了这个）。这里把匹配到的位置也写进日志：万一"遮住了还在点"，
    //   那一行能直接看出它匹到了哪儿。
    Rect matched_rect;
    {
        Matcher matcher(image, DialogBoxSearchRect, m_inst);
        matcher.set_templ(std::string(DialogBoxTemplate));
        matcher.set_threshold(DialogBoxThreshold);
        auto result = matcher.analyze();
        LogInfo << __FUNCTION__ << "| dialog box:" << (result ? "hit" : "miss") << (result ? result->score : 0.0)
                << (result ? result->rect.to_string() : std::string("none"))
                << "roi:" << DialogBoxSearchRect.to_string() << "threshold:" << DialogBoxThreshold;
        if (!result) {
            // ★ 退结算画面：接了自动战斗的那一段（打 0-1 之后）进来时，游戏停在
            //   上一关的结算画面上 —— 那屏没有对话框、也没有日志标志，插件本来会
            //   什么都不做、教程就卡住。官方退出结算的办法就是反复点右上角那个角落
            //   （ClickCornerUntilStartButton 的 [1260,100,10,10]），这里照抄同一个点。
            //
            //   只在【第一次都没看到对话】时点，避免教程正常推进时乱点：
            //   如果这一屏有对话，说明已经在教程流程里了，不该去点角落。
            if (m_settlement_clicks_left < 0) {
                // 还没决定过：这一屏没对话 -> 当成结算画面，装上点击次数
                m_settlement_clicks_left = SettlementMaxClicks;
                LogInfo << __FUNCTION__ << "| no dialog at segment start, treat as settlement, clicks:"
                        << SettlementMaxClicks;
            }
            if (m_settlement_clicks_left > 0) {
                --m_settlement_clicks_left;
                LogInfo << __FUNCTION__ << "| exit settlement, click corner" << SettlementCornerPoint.x
                        << SettlementCornerPoint.y << "left:" << m_settlement_clicks_left;
                click_point(SettlementCornerPoint.x, SettlementCornerPoint.y);
                sleep(GachaSkipIntervalMs);
                return true;
            }

            // ★ 不在对话时，如果正处于"追跳过"状态（刚点过招募干员、在播抽卡动画），
            //   就一直点跳过区，直到重新看到对话为止。
            //   抽卡动画那屏没有对话标志，不这么做的话插件会干等着、教程卡死。
            if (m_gacha_skip_active) {
                if (m_gacha_skip_rounds >= MaxGachaSkipRounds) {
                    LogInfo << __FUNCTION__ << "| gacha skip gave up after" << m_gacha_skip_rounds << "rounds";
                    m_gacha_skip_active = false;
                    m_gacha_skip_rounds = 0;
                    return true;
                }
                ++m_gacha_skip_rounds;
                LogInfo << __FUNCTION__ << "| gacha skip round" << m_gacha_skip_rounds << "/" << MaxGachaSkipRounds
                        << "click" << GachaSkipPoint.x << GachaSkipPoint.y;
                click_point(GachaSkipPoint.x, GachaSkipPoint.y);
                sleep(GachaSkipIntervalMs);
                return true;
            }
            LogInfo << __FUNCTION__ << "| not in a dialogue, do nothing";
            // 离开对话就把状态清掉，下次进来重新从"点掉对话"开始
            m_unchanged_clicks = 0;
            m_prev_dialog_frame = cv::Mat {};
            return true;
        }
        matched_rect = result->rect;
    }

    // 看到对话 -> 说明不在结算画面上了，把"退结算"的计数收掉
    // （留 -1 会让下一轮再没对话时又去点角落；收成 0 就只在真正需要时点一次）
    if (m_settlement_clicks_left > 0) {
        LogInfo << __FUNCTION__ << "| dialogue is back, settlement exit stopped, unused clicks:"
                << m_settlement_clicks_left;
    }
    m_settlement_clicks_left = 0;

    // 重新看到对话 -> 退出"追跳过"状态，回到正常流程
    if (m_gacha_skip_active) {
        LogInfo << __FUNCTION__ << "| gacha skip done, dialogue is back after" << m_gacha_skip_rounds << "rounds";
        m_gacha_skip_active = false;
        m_gacha_skip_rounds = 0;
        m_unchanged_clicks = 0;
        m_prev_dialog_frame = cv::Mat {};
    }

    // ② 判断"上一轮点到对话了没有"。
    //
    // 状态机（按用户的设计）：
    //   · 看到标志 = 在对话 → 点它（官方 rectMove 那个位置）
    //   · 同时看对话框那块画面：和上一轮比变了 → 说明点掉了对话，继续点
    //                          没变       → 说明这句是"需要动手"的，去认文字
    //   · 认文字：OCR 整句 → 和规则表按相似度比 → 选最像的那条 → 做它对应的动作
    //
    // ★ 这么设计的好处：平时【完全不依赖 OCR】，认错字也不会乱点；
    //   OCR 只在"点了没反应"时才用上，用错也只是多试一轮。
    const bool frame_changed = dialog_frame_changed(image, DialogFrameRect);

    if (m_unchanged_clicks < MaxUnchangedClicks) {
        // ★ 点【匹配到的那个框本身】的中心，不要套官方的 rectMove。
        //   踩过：我原来用 offset_by(matched_rect, DialogBoxClickMove) 借官方偏移
        //   [125,20,200,60]，结果点到 (303, 86) —— 屏幕左上角。
        //   原因：官方的 rectMove 是给它自己的"战斗中头像对话框"用的，那个对话框
        //   位置和这里的招募屏不一样，套过来就偏了。
        //   匹配框是官方模板 BattleAvatarDialog.png 在屏幕上的实际位置，点它自己最可靠。
        const int click_x = matched_rect.x + matched_rect.width / 2;
        const int click_y = matched_rect.y + matched_rect.height / 2;
        LogInfo << __FUNCTION__ << "| normal dialogue, click matched box" << matched_rect.to_string() << "->" << click_x
                << click_y << "unchangedClicks:" << m_unchanged_clicks << "frameChanged:" << frame_changed;
        click_point(click_x, click_y);
        if (frame_changed) {
            m_unchanged_clicks = 0; // 点掉了，重新计数
        }
        else {
            ++m_unchanged_clicks; // 没变，累计；够了就去认文字
        }
        return true;
    }

    // ③ 连点几次画面都没变 → 这一句需要动手：OCR 整句，和规则表按相似度比，取最像的
    //
    // ★ 对话框有两种位置：文字在【上方】那条（招募屏）或在【下方】那条（其余屏）。
    //   该读哪一条？—— 不能用标志的位置判断：那个标志（RHODES ISLAND）
    //   在每一屏都匹配在左上角 [78,36]，和对话框在哪里【没有关系】。
    //   踩过的两个错法：
    //     · 写死 use_top = true        → 下方对话时读上方空框，OCR 出乱码；
    //     · 按标志 y<360 判断           → 标志恒在 y=36，永远选"上方"，同样读空框；
    //     · 按"哪边 OCR 字数多"判断     → 下方残留文字（横幅）会被当成正文。
    //   正确的判据是【匹配结果】：两个框各查一次表，哪边能认出规则就用哪边，
    //   两边都认出就取分高的。这条不依赖任何位置假设，最稳。
    const std::vector<std::string> top_lines = ocr_text_box(TopBoxRect);
    const std::vector<std::string> bottom_lines = ocr_text_box(BottomBoxRect);

    double top_score = 0.0;
    double bottom_score = 0.0;
    std::string top_ocr;
    std::string bottom_ocr;
    const Rule* top_rule = lookup(top_lines, &top_score, &top_ocr);
    const Rule* bottom_rule = lookup(bottom_lines, &bottom_score, &bottom_ocr);

    const bool use_top = top_rule != nullptr && (bottom_rule == nullptr || top_score >= bottom_score);
    const Rule* rule = use_top ? top_rule : bottom_rule;
    const double score = use_top ? top_score : bottom_score;
    const Rect& box = use_top ? TopBoxRect : BottomBoxRect;

    const std::string joined_lines = use_top ? top_ocr : bottom_ocr;
    LogInfo << __FUNCTION__ << "| need action, ocr box:" << (use_top ? "top" : "bottom") << box.to_string()
            << "matchedRect:" << matched_rect.to_string() << "topScore:" << top_score << "bottomScore:" << bottom_score
            << "bestScore:" << score << "text:" << joined_lines;
    // 诊断：两个框【各自】读到了什么。用来确认 ROI 框得够不够、OCR 到底认出了什么。
    // ★ 必须打 top_lines/bottom_lines（归一化后的实际行），
    //   不能打 top_ocr/bottom_ocr —— 后者只在匹配成功时才有值，
    //   匹配失败时恒为空，看起来像"一个字都没读到"，会把人带偏（踩过）。
    const auto dump_lines = [](const std::vector<std::string>& lines) {
        std::string text;
        for (const std::string& line : lines) {
            text += line;
            text += '/';
        }
        return text;
    };
    LogInfo << __FUNCTION__ << "| ocr raw | top:" << top_lines.size() << "lines|" << dump_lines(top_lines)
            << " | bottom:" << bottom_lines.size() << "lines|" << dump_lines(bottom_lines);
    if (rule != nullptr) {
        LogInfo << __FUNCTION__ << "| matched rule" << rule->segment << rule->order << "/" << m_rules.size()
                << rule->text << "| score:" << score << "actions:" << rule->actions.size();
        if (!rule->actions.empty()) {
            run_actions(rule->actions);
            // ★ 如果这条是"招募干员"，动作做完会进抽卡动画 —— 那屏没有对话框，
            //   插件本来会干等着、教程就卡住。这里接力进"追跳过"状态：
            //   只要还没重新看到对话，就一直点跳过，直到对话出现为止。
            if (rule->text.find(std::string(GachaTriggerText)) != std::string::npos) {
                m_gacha_skip_active = true;
                m_gacha_skip_rounds = 0;
                LogInfo << __FUNCTION__ << "| gacha skip armed (trigger:" << GachaTriggerText << ")";
            }
            // 动完手重置计数，下一轮重新从"点掉对话"开始判断
            m_unchanged_clicks = 0;
            m_prev_dialog_frame = cv::Mat {};
            return true;
        }
    }
    else {
        LogInfo << __FUNCTION__ << "| no rule matched:" << joined_lines;
    }

    // 表里没有、或那条没有动作：当成普通对话继续点（下一轮重新累计）
    LogInfo << __FUNCTION__ << "| fallback: keep clicking the dialog box";
    const Point point = use_top ? TopContinuePoint : BottomContinuePoint;
    click_point(point.x, point.y);
    m_unchanged_clicks = 0;
    return true;
}

// 比较取样区（对话框那块）和上一轮的差异。返回 true = 画面变了。
bool asst::TutorialProgressTaskPlugin::dialog_frame_changed(const cv::Mat& image, const Rect& area)
{
    const cv::Rect roi(area.x, area.y, area.width, area.height);
    const cv::Rect bounded = roi & cv::Rect(0, 0, image.cols, image.rows);
    if (bounded.width <= 0 || bounded.height <= 0) {
        return false;
    }
    cv::Mat current_gray;
    cv::cvtColor(image(bounded), current_gray, cv::COLOR_BGR2GRAY);

    if (m_prev_dialog_frame.empty() || m_prev_dialog_frame.size() != current_gray.size()) {
        m_prev_dialog_frame = current_gray.clone();
        return true; // 还没采过基准，这一轮不算"没变"
    }

    cv::Mat diff;
    cv::absdiff(m_prev_dialog_frame, current_gray, diff);
    const double mean_diff = cv::mean(diff)[0];
    m_prev_dialog_frame = current_gray.clone();

    LogInfo << __FUNCTION__ << "| dialog frame meanDiff:" << mean_diff << "threshold:" << DialogChangeThreshold;
    return mean_diff >= DialogChangeThreshold;
}

bool asst::TutorialProgressTaskPlugin::load_rules()
{
    if (m_rules_loaded) {
        return !m_rules.empty();
    }
    m_rules_loaded = true;

    const OcrTaskConstPtr carrier = Task.get<OcrTaskInfo>(std::string(TableTaskName));
    if (carrier == nullptr) {
        LogError << __FUNCTION__ << "| rules table is missing:" << TableTaskName;
        return false;
    }

    LogInfo << __FUNCTION__
            << "| TutorialProgressTaskPlugin build=2026-10-01T00:30 features=straight-path,line-similarity";

    for (const std::string& raw : carrier->text) {
        const RawRule parsed = split_rule(raw);
        if (parsed.text.empty()) {
            LogError << __FUNCTION__ << "| malformed rule:" << raw;
            continue;
        }
        Rule rule;
        rule.order = static_cast<int>(m_rules.size()) + 1;
        rule.segment = parsed.segment;
        rule.text = parsed.text;
        rule.actions = parsed.actions;
        // 匹配用归一化后的整句（不再截"前 N 个字"—— 截断既会引入误命中，
        // 又要求 OCR 一字不差；改成整句相似度后两者都解决）
        rule.normalized = normalize_for_match(rule.text);
        // 字符边界表只算一次，匹配时反复用（否则每条规则每次都要重切一遍）
        rule.normalized_offsets = utf8_char_offsets(rule.normalized);
        rule.normalized_chars = rule.normalized_offsets.empty() ? 0 : rule.normalized_offsets.size() - 1;
        m_rules_max_bytes = std::max(m_rules_max_bytes, rule.normalized.size());
        m_rules.emplace_back(std::move(rule));
    }

    if (m_rules.empty()) {
        LogError << __FUNCTION__ << "| rules table is empty";
        return false;
    }
    LogInfo << __FUNCTION__ << "| loaded" << m_rules.size() << "rules";
    return true;
}

std::vector<std::string> asst::TutorialProgressTaskPlugin::ocr_text_box(const Rect& roi) const
{
    if (need_exit()) {
        return {};
    }
    OCRer analyzer(ctrler()->get_image(), roi, m_inst);
    const OCRer::ResultsVecOpt results = analyzer.analyze();
    if (!results || results->empty()) {
        return {};
    }

    // ★ 逐行归一化后各自返回，【不要】拼成一个字符串。
    //   踩过：文字框 ROI 会覆盖到屏幕右侧的「终端」按钮和资源数字，
    //   把 OCR 的每一行拼起来就得到"点击进入编队界面终端+7当前"，
    //   真正的对话文字被无关 UI 文字污染，任何规则都匹配不上了。
    //   日志证据：WordOcr [{ text: 点击进入编队界面, rect: [27,26,233,26] },
    //                      { text: 终端, rect: [699,74,135,71] }, ...]
    //
    // ★★ 排序必须是"先按行（y）、再按列（x）"，不能只按 x ★★
    //   踩过：原来只按 x 排，而换行的对话是左对齐、每行的 x 起点相同，
    //   x 相同就分不出上下，两行顺序会颠倒 —— 拼接出来变成
    //     "是在切尔诺伯格市内进行破坏活动的整合运动乐章的旋律由此开始在您苏醒之后首先要面对的"
    //   （后半句跑到了前面），公共字符只剩 21/45 = 46.7%，达不到 70%，
    //   于是「乐章的旋律由此开始……」那条规则永远匹配不上、第 0 章入口永远点不到。
    //   实机日志证据（top: 3 lines）：
    //     Ma111Sto1y/主题曲/                        ← y=184（顶部干扰文字）
    //     是在切尔诺伯格市内进行破坏活动的整合运动/     ← y=118（第 2 行）
    //     乐章的旋律由此开始在您苏醒之后首先要面对的/     ← y=76 （第 1 行）
    //   按 y 排就恢复成正确的阅读顺序。
    std::vector<TextRect> lines = *results;
    std::sort(lines.begin(), lines.end(), [](const TextRect& lhs, const TextRect& rhs) {
        // 同一行内（y 差距在半行高以内）按 x 从左到右；不同行按 y 从上到下
        if (std::abs(lhs.rect.y - rhs.rect.y) > lhs.rect.height / 2) {
            return lhs.rect.y < rhs.rect.y;
        }
        return lhs.rect.x < rhs.rect.x;
    });

    std::vector<std::string> out;
    out.reserve(lines.size());
    for (const TextRect& line : lines) {
        std::string normalized = normalize_for_match(line.text);
        if (!normalized.empty()) {
            out.emplace_back(std::move(normalized));
        }
    }
    return out;
}

const asst::TutorialProgressTaskPlugin::Rule* asst::TutorialProgressTaskPlugin::lookup(
    const std::vector<std::string>& lines,
    double* score_out,
    std::string* ocr_out) const
{
    const Rule* best = nullptr;
    double best_score = 0.0;
    std::string best_ocr;
    // 诊断用：即使最终没达到阈值，也记一下"最接近的那条有多接近"。
    // 没有这个就只能看到"no rule matched"，无法判断是差一点点还是完全不沾边。
    int near_order = 0;
    double near_ratio = 0.0;
    size_t near_common = 0;
    size_t near_rule_chars = 0;
    std::string near_joined;

    // 候选：每一行单独，以及从第 i 行起连续若干行的拼接
    // （对话可能被 OCR 拆成多行，尤其打字机效果只画出一部分时）。
    // 拼接一律从同一批 OCR 行里按阅读顺序取，避免跨区域乱拼。
    for (size_t begin = 0; begin < lines.size(); ++begin) {
        std::string joined;
        for (size_t end = begin; end < lines.size(); ++end) {
            joined += lines[end];
            // 拼接长度超过最长规则（按字节算）就没有必要再往后接了
            if (joined.size() > m_rules_max_bytes) {
                break;
            }

            // ★ 单位统一成【字符】：对比在字符层做（用 common_length_chars），
            //   阈值也都用字符数。绝不能再用字节做对比 ——
            //   汉字 3 字节，不同汉字常共享首字节（E4/E5/E6/E7），
            //   按字节比 LCS 会把半个汉字算成匹配：实测「点击选中干员」和
            //   「点击首页按钮回到中央大厅」凭空公共出 13 字节（13 不是 3 的倍数），
            //   13/18=72% 越过 70% 线判满分，导致点错规则。
            const std::vector<size_t> joined_offsets = utf8_char_offsets(joined);
            const size_t joined_chars = joined_offsets.empty() ? 0 : joined_offsets.size() - 1;

            for (const Rule& rule : m_rules) {
                const size_t rule_chars = rule.normalized_chars;
                if (rule_chars < MinRuleChars) {
                    continue;
                }
                const size_t common =
                    common_length_chars(rule.normalized, rule.normalized_offsets, joined, joined_offsets);
                const size_t need = std::max<size_t>(MinCommonChars, rule_chars / 2);
                // 记录最接近的一条（不管有没有过阈值）
                if (rule_chars > 0) {
                    const double ratio = static_cast<double>(common) / static_cast<double>(rule_chars);
                    if (ratio > near_ratio) {
                        near_ratio = ratio;
                        near_order = rule.order;
                        near_common = common;
                        near_rule_chars = rule_chars;
                        near_joined = joined;
                    }
                }
                double score = 0.0;
                // 「整句基本完整出现」按比例判：至少 70% 的字符对上就算。
                if (common * 10 >= rule_chars * 7) {
                    score = 1.0;
                }
                else if (common >= need) {
                    score = 2.0 * static_cast<double>(common) / static_cast<double>(rule_chars + joined_chars);
                    if (score < SimilarityThreshold) {
                        continue;
                    }
                }
                else {
                    continue;
                }
                if (score > best_score) {
                    best_score = score;
                    best = &rule;
                    best_ocr = joined;
                    // 诊断：记录究竟是怎么判中的（单位都是字符）
                    LogInfo << __FUNCTION__ << "| candidate order:" << rule.order << "ruleChars:" << rule_chars
                            << "commonChars:" << common << "need:" << need << "score:" << score << "joined:" << joined
                            << "rule:" << rule.normalized;
                }
            }
        }
    }

    if (score_out != nullptr) {
        *score_out = best_score;
    }
    if (ocr_out != nullptr) {
        *ocr_out = best_ocr;
    }
    // 总是打出"最接近的一条"，方便判断是差一点点还是完全不沾边
    LogInfo << __FUNCTION__ << "| nearest order:" << near_order << "common:" << near_common << "/" << near_rule_chars
            << "ratio:" << near_ratio << "joined:" << near_joined;
    return best;
}

void asst::TutorialProgressTaskPlugin::run_actions(const std::vector<std::string>& actions)
{
    for (const std::string& action : actions) {
        if (need_exit()) {
            return;
        }
        // ★ X 是唯一【不带冒号】的动作（它没有参数）。
        //   踩过：这里原来一律要求有冒号，于是规则表里写的 `X` 被当成
        //   "malformed action" 直接 continue 掉，收尾逻辑一次都没执行 ——
        //   日志表现是 `run_actions | malformed action: X`，
        //   然后教程继续无限轮询、队列后面的 Copilot 永远轮不到。
        if (action == FinishActionKind) {
            finish_segment(action);
            sleep(ActionGapMs);
            continue;
        }

        const size_t colon = action.find(':');
        if (colon == std::string::npos) {
            LogError << __FUNCTION__ << "| malformed action:" << action;
            continue;
        }
        const std::string kind = action.substr(0, colon);
        const std::vector<std::string> nums = split(action.substr(colon + 1), PointSeparator);

        if (kind == "W") {
            if (!nums.empty()) {
                if (auto ms = to_int(nums[0]); ms) {
                    sleep(static_cast<unsigned>(*ms));
                    continue;
                }
            }
            LogError << __FUNCTION__ << "| malformed wait:" << action;
        }
        else if (kind == "C") {
            if (nums.size() >= 2) {
                if (auto x = to_int(nums[0]), y = to_int(nums[1]); x && y) {
                    click_point(*x, *y);
                }
                else {
                    LogError << __FUNCTION__ << "| malformed click:" << action;
                }
            }
            else {
                LogError << __FUNCTION__ << "| malformed click:" << action;
            }
        }
        else if (kind == "R") {
            // R:x,y,w,h —— 点这个矩形的中心。比写死一个点稳：
            // 之前把"区域左上角"当点写进表里（813,625 其实是 184x47 按钮的左上角），
            // 结果点在了按钮边缘、游戏没反应。
            if (nums.size() >= 4) {
                auto x = to_int(nums[0]), y = to_int(nums[1]), w = to_int(nums[2]), h = to_int(nums[3]);
                if (x && y && w && h) {
                    click_point(*x + *w / 2, *y + *h / 2);
                }
                else {
                    LogError << __FUNCTION__ << "| malformed rect:" << action;
                }
            }
            else {
                LogError << __FUNCTION__ << "| malformed rect:" << action;
            }
        }
        else if (kind == "G") {
            if (nums.size() >= 5) {
                auto x1 = to_int(nums[0]), y1 = to_int(nums[1]), x2 = to_int(nums[2]), y2 = to_int(nums[3]),
                     ms = to_int(nums[4]);
                if (x1 && y1 && x2 && y2 && ms) {
                    swipe_from_to(*x1, *y1, *x2, *y2, *ms);
                }
                else {
                    LogError << __FUNCTION__ << "| malformed swipe:" << action;
                }
            }
            else {
                LogError << __FUNCTION__ << "| malformed swipe:" << action;
            }
        }
        else {
            LogError << __FUNCTION__ << "| unknown action kind:" << kind;
        }
        sleep(ActionGapMs);
    }
}

/**
 * 结束本段教程：把外层轮询的 next 覆盖成结束点任务（它的 next 为空）。
 *
 * ★ 为什么必须显式结束：教程推进是一条【无限轮询】——
 *   Tutorial@Progress@Loop 是 JustReturn（无条件命中），它的 next 里又含哨兵自己，
 *   所以这条链永远不会自己走完。而核心的任务队列是串行的：前一个任务不结束，
 *   排在后面的 Copilot（打 0-1）就永远轮不到。
 *   实机踩过：认到「选择行动地点。」之后一直重复命中同一条规则、
 *   最后整条任务被 stop / TaskChainStopped，后面的 Copilot 从没开始。
 *
 * ★ 为什么用 override_next 而不是别的办法：
 *   · 往 Loop.next 里塞一个"仅这一屏命中"的任务 —— 不行：Loop 是 JustReturn，
 *     无条件命中，后面的候选永远轮不到（这也是哨兵必须排第一的原因）。
 *   · 把哨兵限次到 0 —— 不行：次数检查在回调之前（ProcessTask.cpp:346），
 *     限 0 会让子任务直接 Runout，而 Runout 用 exceeded_next，语义不对。
 *   · override_next 是官方给的"运行期改任务走向"接口，改完立刻生效：
 *     Scan/Loop 下一轮命中后走 Done，Done 的 next 为空 → 整条链正常结束。
 *   （旧版 Tutorial@Main@Act8 就是靠 next 为空收尾的，见 git 367fc771f。）
 */
void asst::TutorialProgressTaskPlugin::finish_segment(std::string_view reason)
{
    LogInfo << __FUNCTION__ << "| tutorial segment finished, reason:" << reason;
    if (m_progress_task == nullptr) {
        LogError << __FUNCTION__ << "| progress task is null, cannot finish the chain cleanly";
        return;
    }

    // ★ 两个任务的 next 都要覆盖，只改 Loop 是不够的。
    //   为什么：ProcessTask 每轮用的候选列表是「这一轮【实际命中】的那个任务自己的 next」。
    //   每轮真正命中的是哨兵 Scan（它每轮都命中），所以下一轮的候选列表来自 Scan.next ——
    //   只改 Loop.next 的话，下一轮照样回到 Scan，继续无限轮询。
    //   实机证据（只改了 Loop 时）：
    //     override next for task Tutorial@Progress@Loop to [ Done ]
    //     cur_task:"Tutorial@Progress@Scan" to_be_recognized:[Scan, Plot@Skip, Gacha@Skip]   ← 又回哨兵
    for (const std::string_view name : { ScanTaskName, LoopTaskName }) {
        if (!m_progress_task->override_next(std::string(name), { std::string(EndTaskName) })) {
            LogError << __FUNCTION__ << "| override_next failed, task:" << name << "end:" << EndTaskName;
        }
    }
}

bool asst::TutorialProgressTaskPlugin::try_skip_plot()
{
    // ① 右上角认「跳过 ▶」
    {
        Matcher matcher(ctrler()->get_image(), PlotSkipSearchRect, m_inst);
        matcher.set_templ(std::string(PlotSkipTemplate));
        matcher.set_threshold(PlotSkipThreshold);
        auto result = matcher.analyze();
        if (!result) {
            return false;
        }
        LogInfo << __FUNCTION__ << "| plot skip button found:" << result->score << result->rect.to_string()
                << "-> click center";
        click_point(result->rect.x + result->rect.width / 2, result->rect.y + result->rect.height / 2);
    }

    // ② 点完跳过会弹确认框，等它出来再点掉。
    //   官方 SkipThePreBattlePlotConfirm：roi [721,290,149,244] + maskRange [35,255]，照抄。
    //   轮询几次：弹窗有淡入动画，第一张图可能还没出来。
    sleep(PlotSkipConfirmWaitMs);
    for (int i = 0; i < PlotSkipConfirmRetry; ++i) {
        Matcher confirm(ctrler()->get_image(), PlotSkipConfirmSearchRect, m_inst);
        confirm.set_templ(std::string(PlotSkipConfirmTemplate));
        confirm.set_threshold(PlotSkipConfirmThreshold);
        // 掩膜只留比较亮的像素（官方 maskRange [35,255]）—— 去掉按钮外的暗底干扰。
        // 注意签名是 set_mask_range(lower, upper, mask_src, mask_close)，参数是整数不是 Mat。
        confirm.set_mask_range(PlotSkipConfirmMaskRange[0], PlotSkipConfirmMaskRange[1]);
        auto result = confirm.analyze();
        if (result) {
            LogInfo << __FUNCTION__ << "| plot skip confirm found:" << result->score << result->rect.to_string()
                    << "-> click center";
            click_point(result->rect.x + result->rect.width / 2, result->rect.y + result->rect.height / 2);
            sleep(ActionGapMs);
            return true;
        }
        sleep(PlotSkipConfirmWaitMs);
    }

    // 认不到确认框也算处理过了（有的剧情没有确认步骤，点了跳过直接继续），
    // 本轮结束，下一轮重新看画面。
    LogInfo << __FUNCTION__ << "| skip clicked but no confirm dialog found, continue";
    return true;
}

bool asst::TutorialProgressTaskPlugin::click_point(int x, int y) const
{
    LogInfo << __FUNCTION__ << "| click" << x << y;
    return ctrler()->click(Point(x, y));
}

bool asst::TutorialProgressTaskPlugin::swipe_from_to(int x1, int y1, int x2, int y2, int duration) const
{
    LogInfo << __FUNCTION__ << "| swipe" << x1 << y1 << "->" << x2 << y2 << duration << "ms";
    return ctrler()->swipe(Point(x1, y1), Point(x2, y2), duration);
}
