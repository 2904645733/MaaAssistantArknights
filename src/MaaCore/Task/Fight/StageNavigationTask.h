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
    /// 只导航到某一章（0~17 → Episode{N}）：不在地图上选具体关卡、也不切换难度。
    /// 章节导航部分与理智作战完全一致（chapter_wayfinding）。
    /// </summary>
    bool set_chapter(int chapter);

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
    std::string m_stage_code;
    bool m_switch_difficulty_after_stage_selection = false;
    std::shared_ptr<ProcessTask> m_fight_task_ptr = nullptr;
    static constexpr std::string_view AnnihilationSuffix = "Annihilation";
};
}
