#pragma once
#include <vector>

#include "Task/AbstractTask.h"

namespace asst
{
class ProcessTask;

class StageNavigationTask : public AbstractTask
{
public:
    using AbstractTask::AbstractTask;
    virtual ~StageNavigationTask() noexcept override = default;

    bool set_stage_name(const std::string& stage_name);

    /// <summary>
    /// 只导航到某一章（0~17 → Episode{N}）：不在地图上选具体关卡。
    /// 章节导航部分与理智作战完全一致（chapter_wayfinding）。
    /// </summary>
    /// <param name="chapter">章节号（0~17）。</param>
    /// <param name="difficulty">可选难度，只对主线 10~14 章有效："Hard"（磨难）/ "Normal"（标准），其它章节留空。
    /// 传入时会在章节导航之后、找关卡之前跑 ChapterDifficultyHard / ChapterDifficultyNormal，
    /// 与理智作战进入 10~14 章磨难关卡的顺序完全一致（见 StageNavigationTask.cpp 的 get_chapter_difficulty_mode）。</param>
    bool set_chapter(int chapter, const std::string& difficulty = std::string {});

    /// <summary>
    /// 只导航到某个活动（SideStory）：把 prefix + "@EnterSideStoryNew.png" 注入公共任务 EnterSideStoryNew
    /// （全屏找活动入口卡片），再跑 SideStoryNew 链——列表向下滑找入口、点开卡片、OCR「进入活动」。
    /// 同样不选具体关卡。
    /// </summary>
    /// <param name="prefix">活动代号，如 "SL"、"GO"。</param>
    /// <param name="mode">活动内的关卡模式，可选 "EX" / "S"；留空表示不切模式（进活动后默认就是普通关）。
    /// 传入时会在「进入活动」之后再全屏扫对应的模式按钮模板图并点击，
    /// 模板图路径：resource/template/StageNavigation/SideStory/{prefix}/{prefix}@EnterStageMode{mode}.png；
    /// 图不存在就跳过这一步、不影响整条导航。</param>
    bool set_side_story(const std::string& prefix, const std::string& mode = std::string {});

    void set_fight_task_ptr(std::shared_ptr<ProcessTask> fight_task_ptr) noexcept
    {
        m_fight_task_ptr = fight_task_ptr;
    };

protected:
    virtual bool _run() override;
    void clear() noexcept;

    bool chapter_wayfinding();
    bool swipe_and_find_stage();
    bool switch_difficulty_after_stage_selection();

    // 是否有定义任务名的Task
    bool m_is_directly = false;
    // 只导航到某一章（不选具体关卡）
    bool m_chapter_only = false;
    std::string m_directly_task;
    // Not directly
    std::string m_chapter_task;
    std::vector<std::string> m_difficulty_tasks;
    // 活动内的关卡模式入口（EX / S）任务：只扫几次，认不到就跳过，别像难度任务那样重试 20 次
    std::string m_stage_mode_task;
    std::string m_stage_code;
    bool m_switch_difficulty_after_stage_selection = false;
    std::shared_ptr<ProcessTask> m_fight_task_ptr = nullptr;
    static constexpr std::string_view AnnihilationSuffix = "Annihilation";
};
}
