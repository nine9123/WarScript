# ──────────────────────────────────────────────────────
# test_string_interpolation.ws
# Covers: variable interpolation, expressions, function
#         calls, class properties, array access, nested
#         braces, edge cases (start/end/only), assignment
# ──────────────────────────────────────────────────────

# ── Simple variable interpolation ──
name = "world"
assert "hello {name}" == "hello world"

# ── Multiple variables ──
first = "Jane"
age = 30
assert "{first} is {age}" == "Jane is 30"

# ── Interpolation at start ──
x = 42
assert "{x} is the answer" == "42 is the answer"

# ── Interpolation at end ──
assert "answer is {x}" == "answer is 42"

# ── Interpolation only ──
assert "{x}" == "42"

# ── Interpolation with expression ──
a = 10
b = 20
assert "sum: {a + b}" == "sum: 30"

# ── Interpolation with multiplication ──
assert "double: {a * 2}" == "double: 20"

# ── Interpolation with comparison ──
val = 5
assert "check: {val > 3}" == "check: True"
assert "check: {val < 3}" == "check: False"

# ── Interpolation with function call ──
fun double [n]
    return n * 2
end
assert "doubled: {double [7]}" == "doubled: 14"

# ── Interpolation with class property ──
class Hero [name, hp]
end
h = new Hero ["Warrior", 100]
assert "{h :: name} has {h :: hp} hp" == "Warrior has 100 hp"

# ── Interpolation with nested braces (array access) ──
arr = {10, 20, 30}
assert "val: {arr{1}}" == "val: 20"

# ── Interpolation with multiple segments ──
x = 1
y = 2
z = 3
assert "{x},{y},{z}" == "1,2,3"

# ── Interpolation with text between ──
assert "({x}, {y}, {z})" == "(1, 2, 3)"

# ── Interpolation in assignment ──
greeting = "hi {name}"
assert greeting == "hi world"

# ── Interpolation with boolean ──
flag = true
assert "flag is {flag}" == "flag is True"

# ── Interpolation with null ──
nothing = null
assert "value: {nothing}" == "value: null"

# ── Chained interpolation building ──
part1 = "hello"
part2 = "world"
result = "{part1} {part2}!"
assert result == "hello world!"

# ── Interpolation with complex class expression ──
class Point [x, y]
    fun to_string []
        return "({x}, {y})"
    end
end
p = new Point [3, 4]
assert "point = {p :: to_string []}" == "point = (3, 4)"

# ── No interpolation (plain string) ──
assert "no interpolation here" == "no interpolation here"
assert "" == ""

# ── Interpolation with arithmetic chains ──
assert "result: {2 + 3 * 4}" == "result: 14"
assert "result: {(2 + 3) * 4}" == "result: 20"

# ── Interpolation inside loop ──
messages = {}
loop i in 0..3
    messages << "item {i}"
end
assert messages{0} == "item 0"
assert messages{1} == "item 1"
assert messages{2} == "item 2"

# ── Interpolation with conditional-set values ──
status = "unknown"
hp = 80
if hp > 50
    status = "healthy"
end
assert "Status: {status}" == "Status: healthy"

# ── Interpolation preserves spacing ──
spacer = "  "
assert "a{spacer}b" == "a  b"
