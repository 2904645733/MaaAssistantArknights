#pragma once
#include "Task/InterfaceTask.h"

#include <memory>

namespace asst
{
class ProcessTask;
class StageNavigationTask;

/// <summary>
/// 只导航到指定章节的任务：复用理智作战（FightTask）进入选关界面的 StageBegin，
/// 再走 StageNavigationTask 的章节导航部分（Episode{N}）——不在地图上选具体关卡、也不战斗。
/// 供「战斗任务」的导航小任务使用（跨章节连续作战时切换章节）。
/// </summary>
class ChapterNavigationTask final : public InterfaceTask
{
public:
    inline static constexpr std::string_view TaskType = "ChapterNavigation";

    ChapterNavigationTask(const AsstCallback& callback, Assistant* inst);
    virtual ~ChapterNavigationTask() override = default;

    virtual bool set_params(const json::value& params) override;

protected:
    std::shared_ptr<ProcessTask> m_start_up_task_ptr = nullptr;
    std::shared_ptr<StageNavigationTask> m_chapter_navigation_task_ptr = nullptr;
};
}
