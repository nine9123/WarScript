# ──────────────────────────────────────────────────────
# test_deep_string_interpolation.ws
# Deep string interpolation: nested braces (array access
# inside interpolation), method calls, arithmetic,
# class properties, boolean expressions, complex chains
# ──────────────────────────────────────────────────────

# ── 1. Array access inside interpolation ──
arr = {100, 200, 300}
assert "first: {arr{0}}" == "first: 100"
assert "last: {arr{2}}" == "last: 300"

# ── 2. Variable index inside interpolation ──
idx = 1
assert "item[{idx}] = {arr{idx}}" == "item[1] = 200"

# ── 3. Arithmetic inside interpolation ──
a = 10
b = 3
assert "{a} + {b} = {a + b}" == "10 + 3 = 13"
assert "{a} * {b} = {a * b}" == "10 * 3 = 30"
assert "{a} % {b} = {a % b}" == "10 % 3 = 1"

# ── 4. Comparison in interpolation ──
x = 5
assert "x > 3: {x > 3}" == "x > 3: True"
assert "x < 3: {x < 3}" == "x < 3: False"
assert "x == 5: {x == 5}" == "x == 5: True"

# ── 5. Class property in interpolation ──
class Person [name, age]
end
p = new Person ["Alice", 30]
assert "Name: {p :: name}, Age: {p :: age}" == "Name: Alice, Age: 30"

# ── 6. Method call inside interpolation ──
class Greeter [name]
    fun greet []
        return "Hello, " + name + "!"
    end
end
g = new Greeter ["World"]
assert "Says: {g :: greet []}" == "Says: Hello, World!"

# ── 7. Nested class property ──
class Address [city, country]
end
class Contact [name, addr]
end
addr = new Address ["London", "UK"]
contact = new Contact ["Bob", addr]
assert "{contact :: name} lives in {contact :: addr :: city}" == "Bob lives in London"

# ── 8. Interpolation in loop ──
items = {"apple", "banana", "cherry"}
result = {}
loop i in 0..3
    result << "{i + 1}. {items{i}}"
end
assert result{0} == "1. apple"
assert result{1} == "2. banana"
assert result{2} == "3. cherry"

# ── 9. Multiple interpolations with text between ──
h = 12
m = 5
s = 30
assert "{h}:{m}:{s}" == "12:5:30"
assert "Time is {h}h {m}m {s}s" == "Time is 12h 5m 30s"

# ── 10. Interpolation with null ──
nothing = null
assert "value: {nothing}" == "value: null"

# ── 11. Interpolation with boolean ──
flag = true
assert "active: {flag}" == "active: True"
assert "inactive: {!flag}" == "inactive: False"

# ── 12. Interpolation with function call ──
fun format_number [n]
    if n < 10
        return "0" + n
    end
    return "" + n
end
assert "Time: {format_number [3]}:{format_number [15]}" == "Time: 03:15"

# ── 13. Interpolation building complex strings ──
class TableRow [name, value, unit]
    fun format []
        return "| {name} | {value} {unit} |"
    end
end

r1 = new TableRow ["Temperature", 23, "C"]
r2 = new TableRow ["Humidity", 65, "%"]
assert r1 :: format [] == "| Temperature | 23 C |"
assert r2 :: format [] == "| Humidity | 65 % |"

# ── 14. Empty interpolation segments ──
empty = ""
assert "{empty}hello" == "hello"
assert "hello{empty}" == "hello"
assert "{empty}{empty}" == ""

# ── 15. Consecutive interpolations ──
a = "X"
b = "Y"
c = "Z"
assert "{a}{b}{c}" == "XYZ"
assert "{a}-{b}-{c}" == "X-Y-Z"
assert "({a})({b})({c})" == "(X)(Y)(Z)"
