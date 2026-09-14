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

    /// <summary>
    /// 战斗任务的「剿灭导航」：先点选关界面的「剿灭」标签进入剿灭作战页、再点「进入」，
    /// 之后与理智作战切剿灭关卡完全一致：左上角返回 → 右下角切换 → 在关卡列表里认出关卡名并点击。
    /// 整条链写在 tasks.json 的 next 里：
    /// StageAnnihilationTab → StageAnnihilationEnter → AnnihilationNavReturn → AnnihilationNavSwitch
    /// → AnnihilationNavSelectStage（OCR 任务，text 在这里运行时注入）。
    /// </summary>
    /// <param name="stage_name">要切到的剿灭关卡名（如 "龙门市区"），由界面从"这一步后面第一个作战任务的作业"里读出。</param>
    bool set_annihilation(const std::string& stage_name);

    /// <summary>
    /// 战斗任务的「资源关导航」：只切到资源关页面（某个产物的大类），不选具体关卡。
    /// 做法就是直接跑理智作战那个资源关任务（如 "CE-6"）：它会先点「资源」标签进资源关页面
    /// （ResourceStages 的 sub: StageResource），再"认该产物的入口卡片，认不到就左滑 / 右滑"
    /// （CE-6.next = [CE6@StageCE, CE-6@SwipeToTheLeft]；芯片在右边，所以 PR-* 是右滑）。
    /// 唯一的区别：理智作战认到卡片后还会继续选具体关卡，这里把"选具体关卡"那一步的次数限制成 0，
    /// 认到入口卡片、停在关卡列表就算完成。
    /// </summary>
    /// <param name="stage_code">理智作战里的资源关代号（tasks.json 里的任务名），如 "CE-6"、"PR-A-1"。</param>
    bool set_resource(const std::string& stage_code);

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
    // 战斗任务的「剿灭导航」（剿灭标签 → 进入 → 返回 → 切换 → 选关卡）
    bool m_annihilation = false;
    std::string m_annihilation_stage;
    // 战斗任务的「资源关导航」：跑理智作战的资源关任务（资源标签 → 入口卡片 / 认不到就左滑右滑）
    bool m_resource = false;
    std::string m_resource_stage;
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
