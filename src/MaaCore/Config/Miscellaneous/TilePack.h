#pragma once

#include "Common/AsstBattleDef.h"
#include "Common/AsstTypes.h"
#include "Config/AbstractConfig.h"
#include "MaaUtils/Conf.h"

MAA_SUPPRESS_CV_WARNINGS_BEGIN
#include <Arknights-Tile-Pos/TileDef.hpp>
MAA_SUPPRESS_CV_WARNINGS_END

namespace asst
{
class TilePack final : public MAA_NS::SingletonHolder<TilePack>, public AbstractConfig
{
public:
    using LevelKey = Map::LevelKey;
    using LazyMap = std::vector<std::pair<LevelKey, std::filesystem::path>>;

public:
    enum class HeightType
    {
        Invalid = -1,
        Highland = 0,
        Floor = 1
    };
    enum class TileKey
    {
        Invalid = -1,
        Forbidden, // 不能放干员，敌人也不能走
        Wall,      // 可以放高台干员的位置
        Road,      // 可以放地面干员，敌人也可以走
        Home,      // 蓝门（可能还有其他的情况）
        EnemyHome, // 红门（可能还有其他的情况）
        Green,     // 绿门, 怪从绿门进入之后, 在后续阶段出现, 常见于引航者试炼
        Airport,   // 无人机始发站
        Floor,     // 不能放干员，但敌人可以走
        Hole,      // 空降兵掉下去的地方（
        Telin,     // 传送门入口
        Telout,    // 传送门出口
        Grass,     // 草地，带有隐匿，可以放干员，部分地块敌人可以走
        DeepSea,   // 水，不能直接放干员（要借助泳圈道具），敌人可以走，但敌人会持续掉血
        Volcano,   // 岩浆地块，可以放干员，敌人也可以走，但是会持续掉血
        Healing,   // 治疗地块，可以放干员，敌人也可以走，会给干员回血
        Fence,     // 敌人不会走，但可以放干员的地面位置
        Infection, // 源石地板, 可以放干员，敌人也可以走，但是会持续掉血, 且攻击力增加
    };

    struct TileInfo
    {
        battle::LocationType buildable = battle::LocationType::Invalid;
        HeightType height = HeightType::Invalid;
        TileKey key = TileKey::Invalid;
        Point pos; // 像素坐标
        Point loc; // 格子位置
    };

    struct result_type
    {
        std::unordered_map<Point, TileInfo> normal_tile_info;
        std::unordered_map<Point, TileInfo> side_tile_info;
        Point retreat_button;
        Point skill_button;
        bool has_multi_stages = false;
    };

public:
    virtual ~TilePack() override = default;

    template <typename KeyT>
    std::optional<LazyMap::value_type> find(const KeyT& key) const
    {
        // ★ 为什么要分优先级找（上游原实现是"遍历、第一个 match 就返回"）：
        //   不同关卡会共用同一个 code（关卡显示名），此时"谁在前谁赢"会取错数据。
        //   实测（TR-1）：stage_name 是显示名 "TR-1"，overview 里有两条 code=="TR-1"
        //     · tr_01-obt-training-level_training_1.json  （训练关 TR-1, stageId=tr_01）  ← 要这个
        //     · lt_tr_01-obt-legion-lttr-level_lt_tr01.json（集成战略 TR-1, stageId=lt_tr_01）
        //   结果 m_side_tile_info 用上了集成战略那份 view，自动战斗部署坐标全错；
        //   而且改训练关的 view 完全无效（读的根本不是同一个文件）。
        //   同类撞车：0-1 有 main_00-01#f#（突袭）和 main_00-01（普通）两份。
        //
        //   优先级（越靠前越优先）：
        //     ① stageId 精确等于查询串（最可靠的"就是这个关卡"）
        //     ② code 精确等于查询串，且 stageId 不含 "#f#"（排除突袭/困难变体）
        //     ③ code 精确等于查询串（放宽变体）
        //     ④ 原来的 LevelKey::match 任意字段相等
        //   同级里保持 overview 的原有顺序（用 stable 的写法：只记录首个满足更高优先级的）。
        constexpr std::string_view hard_suffix = "#f#";

        auto is_hard_variant = [hard_suffix](const Map::LevelKey& lk) {
            return lk.stageId.find(hard_suffix) != std::string::npos;
        };

        std::optional<LazyMap::value_type> code_match {};
        std::optional<LazyMap::value_type> code_match_training {};
        std::optional<LazyMap::value_type> code_match_hard {};
        std::optional<LazyMap::value_type> loose_match {};

        // "训练关"的 levelId 形如 obt/training/level_training_1。
        // 为什么单独给它一个优先级：显示名撞车里有一类【两条完全不同的关卡却同名】，
        // 例如 "TR-1" 同时是
        //   ① tr_01    / obt/training/level_training_1      （训练关 TR-1，7x6）  ← 要这个
        //   ② lt_tr_01 / obt/legion/lttr/level_lt_tr01      （集成战略 TR-1，10x7）
        // 两者的 #f#、easy_/main_/tough_ 特征都一样，靠那些规则分不开。
        // 实测：全库这类"两条都不带 #f#"的同名撞车里，levelId 含 training 的只有 TR-1/2/3 三组，
        // 所以这条规则只会影响它们，不会动到别的关卡。
        auto is_training = [](const Map::LevelKey& lk) {
            return lk.levelId.find("training") != std::string::npos;
        };

        if constexpr (std::same_as<std::remove_cvref_t<KeyT>, std::string>) {
            for (const auto& pair : m_summarize) {
                const auto& lk = pair.first;
                if (lk.stageId == key) {
                    return pair; // ① 最优先，直接返回
                }
                if (lk.code == key) {
                    if (!code_match.has_value() && !is_hard_variant(lk)) {
                        code_match = pair; // ②
                    }
                    if (!code_match_training.has_value() && !is_hard_variant(lk) && is_training(lk)) {
                        code_match_training = pair; // ③ 训练关优先
                    }
                    if (!code_match_hard.has_value() && is_hard_variant(lk)) {
                        code_match_hard = pair; // ④
                    }
                }
                if (!loose_match.has_value() && lk.match(key)) {
                    loose_match = pair; // ⑤
                }
            }
        }
        else {
            for (const auto& pair : m_summarize) {
                if (pair.first.match(key)) {
                    return pair;
                }
            }
            return std::nullopt;
        }

        if (code_match_training.has_value()) {
            return code_match_training;
        }
        if (code_match.has_value()) {
            return code_match;
        }
        if (code_match_hard.has_value()) {
            return code_match_hard;
        }
        return loose_match;
    }

    template <typename KeyT>
    std::optional<Map::Level> static find_level(const KeyT& key)
    {
        auto file_opt = TilePack::get_instance().find(key);
        if (!file_opt) {
            return {};
        }
        auto json_opt = json::open(file_opt->second);
        if (!json_opt) {
            return {};
        }
        return Map::Level(*json_opt);
    }

    template <typename KeyT>
    result_type static calc(const KeyT& key, double shift_x = 0, double shift_y = 0)
    {
        auto level_opt = find_level(key);
        if (!level_opt) {
            return {};
        }

        return calc_(*level_opt, shift_x, shift_y);
    }

    result_type static calc(const Map::Level& data, double shift_x = 0, double shift_y = 0)
    {
        return calc_(data, shift_x, shift_y);
    }

protected:
    bool parse(const json::value& json) override;

private:
    result_type static calc_(const Map::Level& data, double shift_x, double shift_y);
    LazyMap m_summarize;
};

inline static auto& Tile = TilePack::get_instance();
} // namespace asst
