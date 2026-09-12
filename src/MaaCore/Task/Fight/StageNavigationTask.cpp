#include "StageNavigationTask.h"

#include <boost/regex.hpp>
#include <optional>
#include <ranges>

#include "Config/TaskData.h"
#include "Controller/Controller.h"
#include "Task/ProcessTask.h"
#include "Task/StageNavigationHelper.h"
#include "Utils/Logger.hpp"
#include "Vision/OCRer.h"

namespace
{
enum class ChapterDifficultyMode
{
    Unsupported,
    PreStageNormalHard,
    PostStageNormalHard,
};

std::optional<int> parse_chapter_number(const std::string& chapter)
{
    try {
        return std::stoi(chapter);
    }
    catch (...) {
        return std::nullopt;
    }
}

ChapterDifficultyMode get_chapter_difficulty_mode(int chapter_num)
{
    if (chapter_num >= 10 && chapter_num <= 14) {
        return ChapterDifficultyMode::PreStageNormalHard;
    }
    if (chapter_num >= 15) {
        return ChapterDifficultyMode::PostStageNormalHard;
    }
    return ChapterDifficultyMode::Unsupported;
}
}

bool asst::StageNavigationTask::set_stage_name(const std::string& stage_name)
{
    LogTraceFunction;

    clear();

    // 已有关卡优先使用 tasks.json 中的逻辑
    if (Task.get(stage_name)) {
        m_is_directly = true;
        m_directly_task = stage_name;
        Log.info("directly task", m_directly_task);
        return true;
    }
    m_is_directly = false;

    static const boost::regex stage_regex(R"(^([A-Za-z]{0,3})(\d{1,2})-(\d{1,2})(?:-?(\w+))*$)");
    boost::smatch stage_sm;
    if (!boost::regex_match(stage_name, stage_sm, stage_regex)) {
        Log.error("The stage name is not in invalid, or is not main line stage", stage_name);
        return false;
    }

    // 10-1 -> [1] "", [2] "10", [3] "1", [4] ""
    // JT8-2 -> [1] "JT", [2] "8", [3] "2", [4] ""
    // H10-1-Hard -> [1] "H", [2] "10", [3] "1", [4] "Hard"
    const std::string& stage_prefix = stage_sm[1].str();
    const std::string& chapter = stage_sm[2].str();
    const std::string& stage_index = stage_sm[3].str();
    const std::string& difficulty = stage_sm[4].str();

    static const std::string episode_task_prefix = "Episode";
    m_chapter_task = episode_task_prefix + chapter;
    Log.info("chapter task", m_chapter_task);
    if (!Task.get(m_chapter_task)) {
        Log.error("chapter task not exists", m_chapter_task);
        return false;
    }

    if (!difficulty.empty()) {
        std::string upper_difficulty = difficulty;
        upper_difficulty[0] = static_cast<char>(::toupper(upper_difficulty[0]));
        for (size_t i = 1; i < upper_difficulty.size(); ++i) {
            upper_difficulty[i] = static_cast<char>(::tolower(upper_difficulty[i]));
        }

        const auto chapter_num = parse_chapter_number(chapter);
        if (!chapter_num.has_value()) {
            Log.error("chapter is invalid", chapter);
            return false;
        }

        const auto mode = get_chapter_difficulty_mode(*chapter_num);
        m_switch_difficulty_after_stage_selection = mode == ChapterDifficultyMode::PostStageNormalHard;
        if (mode == ChapterDifficultyMode::PreStageNormalHard) {
            if (upper_difficulty != "Hard" && upper_difficulty != "Normal") {
                Log.error("only Normal/Hard is supported for chapter 10-14", upper_difficulty);
                return false;
            }
            static const std::string difficulty_task_prefix = "ChapterDifficulty";
            m_difficulty_tasks = { difficulty_task_prefix + upper_difficulty };
        }
        else if (mode == ChapterDifficultyMode::PostStageNormalHard) {
            if (upper_difficulty == "Hard") {
                m_difficulty_tasks = { "ChangeToRaidDifficulty", "RaidConfirm" };
            }
            else if (upper_difficulty == "Normal") {
                m_difficulty_tasks = { "ChangeToNormalDifficulty", "NormalConfirm" };
            }
            else {
                Log.error("only Normal/Hard is supported for chapter 15+", upper_difficulty);
                return false;
            }
        }
        else {
            Log.error("difficulty suffix is not supported in this chapter", chapter, upper_difficulty);
            return false;
        }

        for (const auto& difficulty_task : m_difficulty_tasks) {
            Log.info("difficulty task", difficulty_task);
            if (!Task.get(difficulty_task)) {
                Log.error("difficulty task not exists", difficulty_task);
                return false;
            }
        }
    }

    std::string upper_prefix = stage_prefix;
    std::ranges::transform(upper_prefix, upper_prefix.begin(), [](const char ch) -> char {
        return static_cast<char>(::toupper(ch));
    });
    m_stage_code = upper_prefix + chapter + "-" + stage_index;
    Log.info("stage code", m_stage_code);

    return true;
}

bool asst::StageNavigationTask::set_chapter(int chapter, const std::string& difficulty)
{
    LogTraceFunction;

    clear();

    if (chapter < 0 || chapter > 17) {
        Log.error("chapter is invalid", chapter);
        return false;
    }

    m_chapter_only = true;
    m_chapter_task = "Episode" + std::to_string(chapter);
    if (!Task.get(m_chapter_task)) {
        Log.error("chapter task not exists", m_chapter_task);
        return false;
    }

    // 主线 10~14 章有标准 / 磨难两个模式，用的就是理智作战那一套任务
    // （ChapterDifficultyHard / ChapterDifficultyNormal）。这两档难度要在地图上先选好，
    // 所以 m_switch_difficulty_after_stage_selection 保持 false：
    // chapter_wayfinding() 会在 Episode{N}（也就是"前往章节"）之后立刻跑它，顺序与理智作战完全一致。
    if (!difficulty.empty()) {
        if (get_chapter_difficulty_mode(chapter) != ChapterDifficultyMode::PreStageNormalHard) {
            Log.error("difficulty is not supported in this chapter", chapter, difficulty);
            return false;
        }
        if (difficulty != "Hard" && difficulty != "Normal") {
            Log.error("only Hard/Normal is supported for chapter 10-14", difficulty);
            return false;
        }

        m_difficulty_tasks = { "ChapterDifficulty" + difficulty };
        for (const auto& difficulty_task : m_difficulty_tasks) {
            if (!Task.get(difficulty_task)) {
                Log.error("difficulty task not exists", difficulty_task);
                return false;
            }
        }

        Log.info("difficulty task", m_difficulty_tasks.front());
    }

    Log.info("chapter task", m_chapter_task);
    return true;
}

bool asst::StageNavigationTask::set_side_story(const std::string& prefix, const std::string& mode)
{
    LogTraceFunction;

    clear();

    if (prefix.empty()) {
        Log.error("side story prefix is empty");
        return false;
    }

    // 和主线章节同一个套路：xx@SideStoryNew 是 JustReturn 父任务，前缀展开后它的 next 变成
    // [xx@EnterSideStoryNew, xx@SwipeUpToSideStory] ——「找卡片」和「滑动再找」是同级候选，
    // 卡片不在屏幕上时才会落到滑动（直接跑卡片任务的话，找不到卡片就只会原地重试然后报错）。
    // 卡片任务 xx@EnterSideStoryNew 是 38 条已声明派生任务，模板自动取同名图。
    m_chapter_task = prefix + "@SideStoryNew";
    if (!Task.get(m_chapter_task)) {
        Log.error("side story entry task not exists", m_chapter_task);
        return false;
    }

    // 活动内的关卡模式（EX / S，可选）：进入活动之后全屏扫模式按钮的模板图，命中就点。
    // 用的是和活动入口卡片完全相同的「已声明派生任务」机制：tasks.json 里为每个 (代号, 模式)
    // 声明一条空定义（如 "BP@EnterStageModeEX": {}），派生任务自动取同名模板图
    // <代号>@EnterStageMode<模式>.png（放哪个子文件夹都行，TemplResource 按文件名找）。
    // 任务不存在（= 这个活动还没有该模式的图）就跳过，等于不切模式（普通关），不影响整条导航。
    if (!mode.empty()) {
        if (mode != "EX" && mode != "S") {
            Log.error("only EX/S is supported for activity stage mode", mode);
            return false;
        }

        const std::string mode_task = prefix + "@EnterStageMode" + mode;
        if (!Task.get(mode_task)) {
            Log.warn("activity stage mode task not exists, skip mode switch", mode_task);
        }
        else {
            m_stage_mode_task = mode_task;
            Log.info("activity stage mode task", mode, mode_task);
        }
    }

    Log.info("side story entry task", m_chapter_task);

    // 复用"只导航"通道：不选具体关卡（理智作战原有路径完全不受影响）
    m_chapter_only = true;
    return true;
}

bool asst::StageNavigationTask::_run()
{
    LogTraceFunction;

    if (m_is_directly) {
        ProcessTask task(*this, { m_directly_task });
        task.set_retry_times(RetryTimesDefault);
        bool ret = task.run();
        if (!ret && task.get_last_task_name().empty() && m_directly_task.ends_with(AnnihilationSuffix)) {
            m_fight_task_ptr->set_enable(false);
            return true;
        }
        else if (ret && task.get_last_task_name().ends_with("Annihilation@UnableToAgent2")) {
            return false;
        }
        else {
            return ret;
        }
    }

    if (m_chapter_only) {
        // 只做章节导航：与理智作战进入某一章完全一致，不再在地图上选具体关卡、也不切难度。
        // 活动的"点主题曲标签"由活动卡片任务自己的 sub 完成（和 Episode{N} 的结构完全相同）
        return chapter_wayfinding();
    }

    return chapter_wayfinding() && swipe_and_find_stage() && switch_difficulty_after_stage_selection();
}

void asst::StageNavigationTask::clear() noexcept
{
    m_is_directly = false;
    m_chapter_only = false;
    m_directly_task.clear();
    m_chapter_task.clear();
    m_difficulty_tasks.clear();
    m_stage_mode_task.clear();
    m_stage_code.clear();
    m_switch_difficulty_after_stage_selection = false;
}

bool asst::StageNavigationTask::chapter_wayfinding()
{
    LogTraceFunction;

    if (!ProcessTask(*this, { m_chapter_task }).set_retry_times(RetryTimesDefault).run()) {
        return false;
    }

    if (!m_difficulty_tasks.empty() && !m_switch_difficulty_after_stage_selection) {
        if (!ProcessTask(*this, m_difficulty_tasks).set_retry_times(RetryTimesDefault).run()) {
            return false;
        }
    }

    // 活动模式入口（EX / S）：命中就点；认不到就报错、这一步判为失败——选了模式却没切成功，
    // 绝不能悄悄落到普通关去。任务里配了 preDelay 4000（等「进入活动」的转场/黑屏加载），
    // 这里再给 5 次机会（每次间隔约 0.5s），总共约 6 秒窗口。低分匹配日志本来不打印，
    // 所以这里额外写明确的结果日志。
    if (!m_stage_mode_task.empty()) {
        const bool clicked = ProcessTask(*this, { m_stage_mode_task }).set_retry_times(5).run();
        if (!clicked) {
            Log.error("activity stage mode not found", m_stage_mode_task);
            return false;
        }

        Log.info("activity stage mode clicked", m_stage_mode_task);
    }

    return true;
}

bool asst::StageNavigationTask::swipe_and_find_stage()
{
    LogTraceFunction;

    // 优先检查是否存在对应活动关卡名的模板资源，如果存在则走模板匹配
    std::string templ_path = StageNavigationHelper::get_stage_template_path(m_stage_code);
    if (!templ_path.empty()) {
        Log.info("Stage template found, using template matching for", m_stage_code, ", templ:", templ_path);
        Task.get<MatchTaskInfo>(m_stage_code + "@ClickStageByTemplate")->templ_names = { templ_path + ".png" };
        Task.get<OcrTaskInfo>(m_stage_code + "@ClickedCorrectStageByTemplateOrSwipe")->text = { m_stage_code };
        return ProcessTask(*this, { m_stage_code + "@StageNavigationByTemplateMatchBegin" })
            .set_retry_times(RetryTimesDefault)
            .run();
    }

    // 无模板，使用 OCR 匹配
    Task.get<OcrTaskInfo>(m_stage_code + "@ClickStageName")->text = { m_stage_code };
    std::string replace_m_stage_code = m_stage_code;
    utils::string_replace_all_in_place(replace_m_stage_code, { { "-", "" } });
    Task.get<OcrTaskInfo>(m_stage_code + "@ClickedCorrectStage")->text = { m_stage_code, replace_m_stage_code };

    return ProcessTask(*this, { m_stage_code + "@ClickStageName", m_stage_code + "@StageNavigationBegin" })
        .set_retry_times(RetryTimesDefault)
        .run();
}

bool asst::StageNavigationTask::switch_difficulty_after_stage_selection()
{
    LogTraceFunction;

    if (m_difficulty_tasks.empty() || !m_switch_difficulty_after_stage_selection) {
        return true;
    }

    return ProcessTask(*this, m_difficulty_tasks).set_retry_times(RetryTimesDefault).run();
}
