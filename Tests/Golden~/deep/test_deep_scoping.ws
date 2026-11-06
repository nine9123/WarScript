# ──────────────────────────────────────────────────────
# test_deep_scoping.ws
# Deep scoping tests: variable visibility across blocks,
# function closures over globals, if/loop/function
# scope interactions, pre-declaration patterns
# ──────────────────────────────────────────────────────

# ── 1. Global variable visible everywhere ──
global_x = 42
fun read_global_x []
    return global_x
end
assert read_global_x [] == 42

# ── 2. Function modifies global ──
counter = 0
fun bump_counter []
    counter += 1
end
bump_counter []
bump_counter []
bump_counter []
assert counter == 3

# ── 3. Multiple functions share same global ──
shared = 0
fun add_to_shared [n]
    shared += n
end
fun read_shared []
    return shared
end
add_to_shared [10]
add_to_shared [20]
assert read_shared [] == 30

# ── 4. Pre-declared variable modified in if ──
result = "init"
if true
    result = "modified"
end
assert result == "modified"

# ── 5. Pre-declared variable NOT modified in false branch ──
result2 = "init"
if false
    result2 = "should not happen"
end
assert result2 == "init"

# ── 6. Pre-declared variable through elif chain ──
outcome = "default"
val = 50
if val > 100
    outcome = "high"
elif val > 30
    outcome = "medium"
else
    outcome = "low"
end
assert outcome == "medium"

# ── 7. Variable created inside if is LOST after end ──
before_if = "visible"
if true
    inside_if = "only here"
end
# inside_if was created in a new scope — it's null outside
assert inside_if == null

# ── 8. But pre-existing variables are found via parent walk ──
pre_exist = 100
if true
    pre_exist = 200
end
assert pre_exist == 200

# ── 9. Loop variable scope ──
total = 0
loop i in 0..5
    total += i
end
assert total == 10
# Loop counter is lost after loop ends
# (created in loop's own scope)

# ── 10. Nested loops share outer's pre-declared vars ──
outer_count = 0
loop i in 0..3
    loop j in 0..3
        outer_count += 1
    end
end
assert outer_count == 9

# ── 11. Function with same-named parameter as global ──
x = 100
fun shadow_test [x]
    return x * 2
end
assert shadow_test [5] == 10
# Global x unchanged
assert x == 100

# ── 12. Nested function calls with different locals ──
fun outer_func [a]
    return inner_func [a + 1]
end
fun inner_func [b]
    return b * 10
end
assert outer_func [5] == 60

# ── 13. Function returning value used in condition ──
fun is_positive [n]
    return n > 0
end

label = "none"
if is_positive [5]
    label = "positive"
end
assert label == "positive"

label2 = "none"
if is_positive [-3]
    label2 = "positive"
end
assert label2 == "none"

# ── 14. Class methods see constructor parameters ──
class Config [name, value]
    fun get_name []
        return name
    end
    fun get_value []
        return value
    end
    fun set_value [v]
        value = v
    end
end
cfg = new Config ["debug", true]
assert cfg :: get_name [] == "debug"
assert cfg :: get_value [] == true
cfg :: set_value [false]
assert cfg :: get_value [] == false

# ── 15. Multiple class instances don't share scope ──
c1 = new Config ["a", 1]
c2 = new Config ["b", 2]
c1 :: set_value [99]
assert c1 :: get_value [] == 99
assert c2 :: get_value [] == 2

# ── 16. Global modified inside function (not class — class scopes are isolated) ──
event_log = {}
fun log_event [msg]
    event_log << msg
end
log_event ["start"]
log_event ["process"]
log_event ["end"]
assert event_log == {"start", "process", "end"}

# ── 17. Begin/rescue scope isolation ──
rescue_result = "none"
begin
    inside_begin = "created"
    raise "test"
rescue e
    rescue_result = "caught: " + e
end
assert rescue_result == "caught: test"
# inside_begin is lost (created in begin's scope)
assert inside_begin == null

# ── 18. Ensure sees pre-declared variables ──
ensure_flag = false
begin
    x = 1
ensure
    ensure_flag = true
end
assert ensure_flag

# ── 19. Complex: function modifying array through multiple calls ──
data = {}
fun collect [arr, val]
    arr << val
end
loop i in 0..5
    collect [data, i * i]
end
assert data == {0, 1, 4, 9, 16}

# ── 20. Variable type changes across scopes ──
dynamic = 42
assert dynamic == 42
dynamic = "now a string"
assert dynamic == "now a string"
fun change_dynamic []
    dynamic = true
end
change_dynamic []
assert dynamic == true
