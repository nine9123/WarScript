# ──────────────────────────────────────────────────────
# test_deep_operators.ws
# Deep operator interaction tests: exercises all the
# bug-fixed patterns (!!x, !(expr), parens around and/or,
# !obj :: method, "literal"{i}, this :: prop{i}),
# complex precedence chains, all operators combined
# ──────────────────────────────────────────────────────

# ── 1. Double not in various contexts ──
assert !!true == true
assert !!false == false
assert !!true and true
assert !(!!false)
x = false
assert !!x == false
x = true
assert !!x == true

# ── 2. !(complex expressions) ──
assert !(3 > 5)
assert !(10 == 20)
assert !("abc" == "def")
assert !(false or false)
assert !(true and false)
assert !(1 + 1 == 3)
assert !(2 * 3 > 10)

# ── 3. Parenthesized and/or ──
assert (true and true)
assert (true or false)
assert (false or true)
assert !(false and false)
assert !(false or false)
assert (5 > 3 and 10 > 7)
assert (5 > 3 or 10 < 7)
assert !(5 < 3 and 10 > 7)
assert (5 < 3 or 10 > 7)

# ── 4. Nested parenthesized boolean expressions ──
a = true
b = false
c = true
assert (a and (b or c))
assert ((a or b) and (b or c))
assert !((a and b) and c)
assert (a or (b and c))

# ── 5. String literal indexing ──
assert "hello"{0} == "h"
assert "hello"{4} == "o"
assert "world"{2} == "r"
assert "a"{0} == "a"
assert "abcdef"{3} == "d"

# ── 6. Not before class property access ──
class Flag [active]
    fun is_active []
        return this :: active
    end
    fun is_inactive []
        return !this :: active
    end
end

on = new Flag [true]
off = new Flag [false]
assert on :: is_active []
assert !on :: is_inactive []
assert off :: is_inactive []
assert !off :: is_active []

# ── 7. Not before instanceof ──
class Animal
end
class Dog : Animal
end
class Cat : Animal
end

dog = new Dog
cat = new Cat
assert !(dog is Cat)
assert !(cat is Dog)
assert dog is Animal and cat is Animal
assert !(dog is Cat) and dog is Animal

# ── 8. Complex arithmetic precedence ──
assert 2 + 3 * 4 - 1 == 13
assert (2 + 3) * (4 - 1) == 15
assert 100 / 10 % 3 == 1
assert 2 + 10 % 3 == 3
assert 10 - 2 * 3 + 1 == 5

# ── 9. Mixed comparison and logical with parens ──
x = 50
assert (x > 10 and x < 100)
assert (x >= 50 and x <= 50)
assert !(x < 10 or x > 100)
assert (x == 50 or x == 60)
assert !(x != 50 and x != 51)

# ── 10. All comparison operators in single expression ──
a = 5
assert a == 5
assert a != 4
assert a > 4
assert a >= 5
assert a < 6
assert a <= 5
assert (a > 0 and a < 10 and a != 3 and a >= 5 and a <= 5)

# ── 11. Compound assignment with complex RHS ──
x = 10
x += 2 * 3
assert x == 16
x -= 1 + 1
assert x == 14
x *= 1 + 1
assert x == 28
x /= 2 + 2
assert x == 7

# ── 12. Array operations with all arithmetic ──
arr = {}
arr << 2 + 3
arr << 10 - 3
arr << 2 * 4
arr << 20 / 5
arr << 17 % 5
assert arr == {5, 7, 8, 4, 2}

# ── 13. String concatenation mixed with arithmetic ──
assert "(" + (3 + 4) + ")" == "(7)"
assert "2*3=" + 2 * 3 == "2*3=6"
assert "neg: " + -5 == "neg: -5"

# ── 14. Not with equality checks ──
assert !(null == 5)
assert !(5 == null)
assert !(null != null)
assert !("a" == "b")
assert !(true == false)

# ── 15. Combination of all operator types in one test ──
class Box [val]
    fun get []
        return this :: val
    end
end
b = new Box [10]
assert b :: get [] + 5 * 2 == 20
assert b :: get [] > 5 and b :: get [] < 20
assert !(b :: get [] > 100)
assert b is Box
assert !(b is Animal)
b :: val = b :: get [] + 5
assert b :: get [] == 15
