#pragma once
#include "Task/AbstractTask.h"
#include "Task/BattleHelper.h"

#include "Common/AsstBattleDef.h"
#include "Common/AsstTypes.h"
#include "Config/Miscellaneous/TilePack.h"

#include <chrono>

namespace asst
{
class BattleProcessTask : public AbstractTask, public BattleHelper
{
public:
    BattleProcessTask(const AsstCallback& callback, Assistant* inst, std::string_view task_chain);
    virtual ~BattleProcessTask() override = default;

    virtual bool set_stage_name(const std::string& stage_name) override;
    void set_wait_until_end(bool wait_until_end);
    void set_formation_task_ptr(std::shared_ptr<std::unordered_map<battle::OperNameTag, std::string>> value);

protected:
    virtual bool _run() override;

    virtual AbstractTask& this_task() override { return *this; }

    virtual void clear() override;

    virtual bool
        do_derived_action([[maybe_unused]] const battle::copilot::Action& action, [[maybe_unused]] size_t index)
    {
        return false;
    }

    virtual battle::copilot::CombatData& get_combat_data() { return m_combat_data; }

    virtual bool need_to_wait_until_end() const { return m_need_to_wait_until_end; }

    bool to_group();
    bool do_action(const battle::copilot::Action& action, size_t index);

    // 部署区（下方那排卡）有变化就提前认卡：把"轮到这一步才认卡"的开销挪到等条件期间
    bool need_early_deployment_update(const cv::Mat& image, const cv::Mat& image_prev);

    // 将作业步骤中的name转换为具体干员名
    battle::OperNameTag get_name_from_group(battle::Role role, const std::string& oper_name_in_action);
    void notify_action(const battle::copilot::Action& action);
    bool wait_condition(const battle::copilot::Action& action);
    bool enter_bullet_time(battle::Role role, const std::string& name, const Point& location);
    void sleep_and_do_strategy(unsigned millisecond);

    battle::copilot::CombatData m_combat_data;
    std::unordered_map</*group*/ battle::OperNameTag, /*oper*/ battle::OperNameTag> m_oper_in_group;

    bool m_in_bullet_time = false;
    bool m_need_to_wait_until_end = false;
    std::chrono::steady_clock::time_point m_last_deployment_update {}; // 上次"提前认卡"的时间，用于限流
    std::shared_ptr<std::unordered_map<battle::OperNameTag, std::string>> m_formation_ptr = nullptr;
};
}
