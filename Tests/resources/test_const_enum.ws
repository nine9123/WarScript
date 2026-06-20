# ── 1. Basic const ──
const MAX_HP = 100
const TEAM_NAME = "Red"
const IS_ENABLED = true

assert MAX_HP == 100
assert TEAM_NAME == "Red"
assert IS_ENABLED == true

# ── 2. Const in expressions ──
const BASE_DAMAGE = 10
const CRIT_MULTIPLIER = 2.5
damage = BASE_DAMAGE * CRIT_MULTIPLIER
assert damage == 25

# ── 3. Basic enum — access via :: ──
enum DamageType
    PHYSICAL
    MAGICAL
    TRUE
end

assert DamageType :: PHYSICAL == 0
assert DamageType :: MAGICAL == 1
assert DamageType :: TRUE == 2

# ── 4. Enum with explicit values ──
enum State
    IDLE = 0
    RUNNING = 1
    JUMPING = 5
    FALLING = 6
end

assert State :: IDLE == 0
assert State :: RUNNING == 1
assert State :: JUMPING == 5
assert State :: FALLING == 6

# ── 5. Enum values auto-increment from last explicit value ──
enum Priority
    LOW = 10
    MEDIUM
    HIGH
    CRITICAL = 100
    EMERGENCY
end

assert Priority :: LOW == 10
assert Priority :: MEDIUM == 11
assert Priority :: HIGH == 12
assert Priority :: CRITICAL == 100
assert Priority :: EMERGENCY == 101

# ── 6. Enum name[] — reverse lookup for string names ──
assert DamageType :: name [0] == "PHYSICAL"
assert DamageType :: name [1] == "MAGICAL"
assert DamageType :: name [2] == "TRUE"
assert DamageType :: name [99] == "unknown"

# ── 7. Enum name[] with explicit values ──
assert State :: name [State :: JUMPING] == "JUMPING"
assert State :: name [State :: FALLING] == "FALLING"
assert Priority :: name [Priority :: CRITICAL] == "CRITICAL"

# ── 8. Using enum values in conditionals ──
enum Team
    RED
    BLUE
    GREEN
end

fun get_team_name [team]
    if team == Team :: RED
        return "Red"
    elif team == Team :: BLUE
        return "Blue"
    elif team == Team :: GREEN
        return "Green"
    end
    return "Unknown"
end

assert get_team_name [Team :: RED] == "Red"
assert get_team_name [Team :: BLUE] == "Blue"
assert get_team_name [Team :: GREEN] == "Green"

# ── 9. Const used as function default ──
const DEFAULT_SPEED = 5
fun move [unit, speed = DEFAULT_SPEED]
    return speed
end
assert move ["player"] == 5
assert move ["player", 10] == 10

# ── 10. Enum values in arrays ──
valid_types = {DamageType :: PHYSICAL, DamageType :: MAGICAL}
assert valid_types{0} == 0
assert valid_types{1} == 1

# ── 11. Multiple enums don't collide ──
enum Color
    RED
    GREEN
    BLUE
end
assert Color :: RED == 0
assert Team :: RED == 0

# ── 12. Const with computed expression ──
const HALF_HP = MAX_HP / 2
assert HALF_HP == 50

# ── 13. Enum name[] used for display ──
current_type = DamageType :: MAGICAL
label = "Damage: " + DamageType :: name [current_type]
assert label == "Damage: MAGICAL"

# ── 14. Enum values, names, count properties ──
assert DamageType :: count == 3
assert DamageType :: values{0} == 0
assert DamageType :: values{1} == 1
assert DamageType :: values{2} == 2
assert DamageType :: names{0} == "PHYSICAL"
assert DamageType :: names{1} == "MAGICAL"
assert DamageType :: names{2} == "TRUE"

# ── 15. Enum with explicit values: values array reflects explicit ──
assert State :: values{0} == 0
assert State :: values{1} == 1
assert State :: values{2} == 5
assert State :: values{3} == 6
assert State :: count == 4

# ── 16. Loop over enum values ──
total = 0
loop v in DamageType :: values
    total = total + v
end
assert total == 3

# ── 17. Loop over enum names ──
all_names = ""
loop n in DamageType :: names
    if all_names != ""
        all_names = all_names + ","
    end
    all_names = all_names + n
end
assert all_names == "PHYSICAL,MAGICAL,TRUE"

# ── 18. Loop with index using values and names together ──
loop i in 0..DamageType :: count
    v = DamageType :: values{i}
    n = DamageType :: names{i}
    assert DamageType :: name [v] == n
end

print "all const and enum tests passed"
