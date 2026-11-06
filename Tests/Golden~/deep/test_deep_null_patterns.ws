# Null handling patterns: null-safe access, null in collections,
# null in class hierarchies, null propagation, default values

# ── 1. Null-safe function pattern ──
fun safe_div [a, b]
    if b == 0
        return null
    end
    return a / b
end

assert safe_div [10, 2] == 5
assert safe_div [10, 0] == null
assert safe_div [0, 5] == 0

# ── 2. Default value pattern ──
fun with_default [val, default_val]
    if val == null
        return default_val
    end
    return val
end

assert with_default [null, 42] == 42
assert with_default [0, 42] == 0
assert with_default ["", 42] == ""
assert with_default [false, 42] == false
assert with_default [10, 42] == 10

# ── 3. Null in array operations ──
arr = {1, null, 3, null, 5}
non_null = {}
loop item in arr
    if item != null
        non_null << item
    end
end
assert non_null == {1, 3, 5}

# ── 4. Count nulls in array ──
null_count = 0
loop item in arr
    if item == null
        null_count += 1
    end
end
assert null_count == 2

# ── 5. Null class property access ──
class Container [value]
    fun get_or_default [default_val]
        if this :: value == null
            return default_val
        end
        return this :: value
    end
end

c1 = new Container [null]
c2 = new Container [42]
assert c1 :: get_or_default ["N/A"] == "N/A"
assert c2 :: get_or_default ["N/A"] == 42

# ── 6. Null in conditional chains ──
fun classify_value [val]
    if val == null
        return "null"
    elif val == true or val == false
        return "boolean"
    elif val == 0
        return "zero"
    else
        return "other"
    end
end

assert classify_value [null] == "null"
assert classify_value [true] == "boolean"
assert classify_value [false] == "boolean"
assert classify_value [0] == "zero"
assert classify_value [42] == "other"
assert classify_value ["hi"] == "other"

# ── 7. Null propagation through functions ──
fun step1 [x]
    if x == null
        return null
    end
    return x + 1
end
fun step2 [x]
    if x == null
        return null
    end
    return x * 2
end

assert step2 [step1 [5]] == 12
assert step2 [step1 [null]] == null
assert step1 [step2 [null]] == null

# ── 8. Optional class fields ──
class Person [name, age, email]
end

# Only pass 2 args — email will be null
p = new Person ["Alice", 30]
assert p :: name == "Alice"
assert p :: age == 30
assert p :: email == null

# ── 9. Null in array equality ──
assert {null} == {null}
assert {1, null} == {1, null}
assert {null, 2} != {null, 3}
assert {null} != {}
assert {} != {null}

# ── 10. Replace nulls in array ──
data = {1, null, 3, null, 5}
loop i in 0..5
    if data{i} == null
        data{i} = 0
    end
end
assert data == {1, 0, 3, 0, 5}
