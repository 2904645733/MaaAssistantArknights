#pragma once
#include "Common/AsstBattleDef.h"
#include "Config/AbstractConfig.h"

namespace asst
{
class CopilotConfig : public MAA_NS::SingletonHolder<CopilotConfig>, public AbstractConfig
{
public:
    static battle::copilot::BasicInfo parse_basic_info(const json::value& json);
    static std::optional<asst::battle::OperUsage> parse_oper_usage(const json::value& json);
    static std::optional<asst::battle::copilot::OperUsageGroups> parse_groups(const json::value& json);
    static std::optional<std::vector<battle::copilot::Action>> parse_actions(const json::value& json);
    static battle::RoleCounts parse_role_counts(const json::value& json);
    static battle::DeployDirection string_to_direction(const std::string& str);

public:
    virtual ~CopilotConfig() override = default;

    const battle::copilot::CombatData& get_data() const noexcept { return m_data; }

    const std::string& get_stage_name() const noexcept { return m_data.info.stage_name; }

    // [TUTORIAL-AUTOFORMATION] 作业 json 里的 "autoFormation"（缺省 false）。
    // 见 AsstBattleDef.h 里 CombatData::auto_formation 与 CopilotConfig.cpp 的说明。
    bool get_auto_formation() const noexcept { return m_data.auto_formation; }

    void clear();

protected:
    virtual bool parse(const json::value& json) override;

    battle::copilot::CombatData m_data;
};

inline static auto& Copilot = CopilotConfig::get_instance();
}
