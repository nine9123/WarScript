# Class method combinations: methods calling other methods,
# methods using this, methods with complex control flow,
# methods modifying array properties, method chaining via vars

# ── 1. Method calling sibling method ──
class Calc []
    fun add [a, b]
        return a + b
    end
    fun double_add [a, b]
        return this :: add [a, b] * 2
    end
    fun sum_range [n]
        total = 0
        loop i in 1..n + 1
            total = this :: add [total, i]
        end
        return total
    end
end

c = new Calc
assert c :: add [3, 4] == 7
assert c :: double_add [3, 4] == 14
assert c :: sum_range [5] == 15

# ── 2. Method modifying array property ──
class TodoList []
    items = {}
    done = {}

    fun add_todo [text]
        this :: items << text
    end

    fun complete [index]
        item = this :: items{index}
        this :: done << item
    end

    fun pending_count []
        total = 0
        loop i in this :: items
            total += 1
        end
        done_n = 0
        loop d in this :: done
            done_n += 1
        end
        return total - done_n
    end
end

todo = new TodoList
todo :: add_todo ["write tests"]
todo :: add_todo ["fix bugs"]
todo :: add_todo ["deploy"]
assert todo :: pending_count [] == 3
todo :: complete [0]
assert todo :: done == {"write tests"}
assert todo :: pending_count [] == 2

# ── 3. Method with conditional logic ──
class Validator []
    fun validate_age [age]
        if age < 0
            return "invalid: negative"
        elif age < 18
            return "minor"
        elif age < 65
            return "adult"
        else
            return "senior"
        end
    end

    fun validate_name [name, len]
        if len == 0
            return "invalid: empty"
        elif len < 2
            return "invalid: too short"
        elif len > 50
            return "invalid: too long"
        end
        return "valid"
    end
end

v = new Validator
assert v :: validate_age [25] == "adult"
assert v :: validate_age [10] == "minor"
assert v :: validate_age [70] == "senior"
assert v :: validate_age [-1] == "invalid: negative"

assert v :: validate_name ["Al", 2] == "valid"
assert v :: validate_name ["", 0] == "invalid: empty"
assert v :: validate_name ["A", 1] == "invalid: too short"

# ── 4. Method returning class instance ──
class Pair [first, second]
    fun swap []
        return new Pair [this :: second, this :: first]
    end
    fun to_string []
        return "(" + this :: first + ", " + this :: second + ")"
    end
end

p1 = new Pair [1, 2]
assert p1 :: to_string [] == "(1, 2)"
p2 = p1 :: swap []
assert p2 :: to_string [] == "(2, 1)"
assert p1 :: to_string [] == "(1, 2)"

# ── 5. Chained method calls via intermediate vars ──
class Builder []
    parts = {}

    fun add [part]
        this :: parts << part
    end

    fun result []
        out = ""
        loop p in this :: parts
            out += p
        end
        return out
    end
end

b = new Builder
b :: add ["Hello"]
b :: add [" "]
b :: add ["World"]
assert b :: result [] == "Hello World"

# ── 6. Method with loop and early return ──
class Finder []
    fun find_first_gt [arr, threshold, n]
        loop i in 0..n
            if arr{i} > threshold
                return arr{i}
            end
        end
        return null
    end

    fun count_gt [arr, threshold, n]
        count = 0
        loop i in 0..n
            if arr{i} > threshold
                count += 1
            end
        end
        return count
    end
end

f = new Finder
assert f :: find_first_gt [{1, 5, 3, 8, 2}, 4, 5] == 5
assert f :: find_first_gt [{1, 2, 3}, 10, 3] == null
assert f :: count_gt [{1, 5, 3, 8, 2}, 3, 5] == 2
assert f :: count_gt [{1, 2, 3}, 10, 3] == 0

# ── 7. Method accessing constructor parameters ──
class Range [lo, hi]
    fun contains [val]
        return val >= lo and val <= hi
    end
    fun size []
        return hi - lo
    end
    fun overlaps [other]
        return lo < other :: hi and hi > other :: lo
    end
end

r1 = new Range [0, 10]
r2 = new Range [5, 15]
r3 = new Range [20, 30]

assert r1 :: contains [5]
not_in = r1 :: contains [15]
assert !not_in
assert r1 :: size [] == 10
assert r1 :: overlaps [r2]
overlap_r3 = r1 :: overlaps [r3]
assert !overlap_r3
