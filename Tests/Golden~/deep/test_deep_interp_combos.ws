# Interpolation combined with all language features:
# class methods, array access, arithmetic, conditionals,
# loop-generated strings, nested interpolation

# ── 1. Interpolation with all types ──
n = 42
s = "hello"
b = true
nl = null
assert "n={n} s={s} b={b} nl={nl}" == "n=42 s=hello b=True nl=null"

# ── 2. Interpolation with arithmetic ──
x = 10
y = 3
assert "{x}+{y}={x + y}" == "10+3=13"
assert "{x}-{y}={x - y}" == "10-3=7"
assert "{x}*{y}={x * y}" == "10*3=30"
assert "{x}%{y}={x % y}" == "10%3=1"

# ── 3. Interpolation with comparison results ──
a = 5
assert "a>3: {a > 3}" == "a>3: True"
assert "a<3: {a < 3}" == "a<3: False"
assert "a==5: {a == 5}" == "a==5: True"

# ── 4. Interpolation with class properties ──
class Player [name, level, hp]
end
p = new Player ["Hero", 10, 100]
assert "{p :: name} (Lv.{p :: level}) HP:{p :: hp}" == "Hero (Lv.10) HP:100"

# ── 5. Interpolation with method calls ──
class Formatter [prefix]
    fun format [msg]
        return "{prefix}: {msg}"
    end
end
f = new Formatter ["LOG"]
assert f :: format ["started"] == "LOG: started"
assert f :: format ["done"] == "LOG: done"

# ── 6. Interpolation in loop building table ──
rows = {}
loop i in 0..3
    rows << "row {i}: {i * 10}"
end
assert rows{0} == "row 0: 0"
assert rows{1} == "row 1: 10"
assert rows{2} == "row 2: 20"

# ── 7. Interpolation with array access ──
colors = {"red", "green", "blue"}
assert "first={colors{0}}" == "first=red"
assert "last={colors{2}}" == "last=blue"

idx = 1
assert "idx {idx} is {colors{idx}}" == "idx 1 is green"

# ── 8. Interpolation in function return ──
fun greeting [name, time]
    return "Good {time}, {name}!"
end
assert greeting ["Alice", "morning"] == "Good morning, Alice!"
assert greeting ["Bob", "evening"] == "Good evening, Bob!"

# ── 9. Multiple consecutive interpolations ──
a = "X"
b = "Y"
c = "Z"
assert "{a}{b}{c}" == "XYZ"
assert "[{a}][{b}][{c}]" == "[X][Y][Z]"
assert "{a}-{b}-{c}" == "X-Y-Z"

# ── 10. Interpolation with conditional value ──
score = 85
grade = "A"
if score < 90
    grade = "B"
end
assert "Score: {score} Grade: {grade}" == "Score: 85 Grade: B"

# ── 11. Empty interpolation segments ──
empty = ""
assert "{empty}test" == "test"
assert "test{empty}" == "test"

# ── 12. Interpolation with negation ──
val = -42
assert "value: {val}" == "value: -42"
pos = 10
assert "neg: {-pos}" == "neg: -10"
