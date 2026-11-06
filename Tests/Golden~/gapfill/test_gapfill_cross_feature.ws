# ──────────────────────────────────────────────────────
# test_gapfill_cross_feature.ws
# Gap: existing tests cover features in isolation but
# miss interactions between them. These tests exercise
# combinations that could expose integration bugs.
# ──────────────────────────────────────────────────────

# ════════════════════════════════════════════════
#  String interpolation + class properties
# ════════════════════════════════════════════════

class Person[name, age]
end

p = new Person["Alice", 30]
greeting = "{p :: name} is {p :: age}"
assert greeting == "Alice is 30"

# ── Interpolation with method call ──
class Rect[w, h]
    fun area
        return this :: w * this :: h
    end
end
r = new Rect[3, 4]
msg = "Area is {r :: area[]}"
assert msg == "Area is 12"

# ════════════════════════════════════════════════
#  Array of classes with equality checks
# ════════════════════════════════════════════════

class Vec2[x, y]
end

arr = {new Vec2[1, 2], new Vec2[3, 4], new Vec2[5, 6]}
target = new Vec2[3, 4]
found = false
loop item in arr
    if item == target
        found = true
        break
    end
end
assert found == true

# ── Not-equals in loop ──
others = {}
loop item in arr
    if item != target
        others << item
    end
end
assert Array_length[others] == 2

# ════════════════════════════════════════════════
#  Exception handling + return values
# ════════════════════════════════════════════════

fun safe_divide[a, b]
    begin
        if b == 0
            raise "division by zero"
        end
        return a / b
    rescue err
        return err
    end
end

assert safe_divide[10, 2] == 5
assert safe_divide[10, 0] == "division by zero"

# ── Exception in loop ──
fun first_valid_result[values]
    loop item in values
        begin
            if item < 0
                raise "negative"
            end
            return item * 2
        rescue e
            # skip, try next
        end
    end
    return null
end
assert first_valid_result[{-1, -2, 3, 4}] == 6

# ════════════════════════════════════════════════
#  Compound assignment on class array properties
# ════════════════════════════════════════════════

class Inventory[items]
    fun add_item[item]
        this :: items << item
    end
end

inv = new Inventory[{}]
inv :: add_item["sword"]
inv :: add_item["shield"]
assert Array_length[inv :: items] == 2
assert inv :: items{0} == "sword"
assert inv :: items{1} == "shield"

# ════════════════════════════════════════════════
#  Nested function calls with same parameter names
# ════════════════════════════════════════════════

fun add[a, b]
    return a + b
end

fun multiply[a, b]
    return a * b
end

# Parameters named 'a' and 'b' in both functions
# should not interfere
assert add[multiply[2, 3], multiply[4, 5]] == 26

# ── Recursive function parameter isolation ──
fun factorial[n]
    if n <= 1
        return 1
    end
    return n * factorial[n - 1]
end
assert factorial[5] == 120
assert factorial[1] == 1
assert factorial[0] == 1

# ════════════════════════════════════════════════
#  String operations inside class methods
# ════════════════════════════════════════════════

class StringBuilder[value]
    fun append[text]
        this :: value = this :: value + text
    end

    fun prepend[text]
        this :: value = text + this :: value
    end

    fun get
        return this :: value
    end
end

sb = new StringBuilder[""]
sb :: append["hello"]
sb :: append[" "]
sb :: append["world"]
assert sb :: get[] == "hello world"
sb :: prepend[">>> "]
assert sb :: get[] == ">>> hello world"

# ════════════════════════════════════════════════
#  Loop with break inside exception handler
# ════════════════════════════════════════════════

found_at = -1
loop i in 0..10
    begin
        if i == 5
            raise "found"
        end
    rescue e
        found_at = i
        break
    end
end
assert found_at == 5

# ════════════════════════════════════════════════
#  Ensure block runs even with return
# ════════════════════════════════════════════════

cleanup_ran = false
fun with_ensure
    begin
        return 42
    ensure
        cleanup_ran = true
    end
end
result = with_ensure[]
assert result == 42
assert cleanup_ran == true

# ════════════════════════════════════════════════
#  Deeply nested scope access
# ════════════════════════════════════════════════

outer = 0
if true
    if true
        if true
            loop j in 0..5
                if j == 3
                    outer = j
                end
            end
        end
    end
end
assert outer == 3

# ════════════════════════════════════════════════
#  Array append in loop with class instances
# ════════════════════════════════════════════════

class Pair[a, b]
end

pairs = {}
loop i in 0..3
    loop j in 0..3
        if i < j
            pairs << new Pair[i, j]
        end
    end
end
# (0,1) (0,2) (1,2) = 3 pairs
assert Array_length[pairs] == 3
assert pairs{0} :: a == 0
assert pairs{0} :: b == 1
assert pairs{2} :: a == 1
assert pairs{2} :: b == 2
