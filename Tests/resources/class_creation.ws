# ── 1. Multiple instances of the same class in a loop ──
class Point [x, y]
end

points = {}
loop i in 0..5
    p = new Point [i, i * 10]
    points << p
end

assert points{0} :: x == 0
assert points{0} :: y == 0
assert points{1} :: x == 1
assert points{1} :: y == 10
assert points{4} :: x == 4
assert points{4} :: y == 40

# ── 2. Instances are independent (no shared state) ──
a = new Point [1, 2]
b = new Point [3, 4]
a :: x = 99
assert a :: x == 99
assert b :: x == 3

# ── 3. Multiple instances of a class with methods ──
class Counter [n]
    fun increment []
        n = n + 1
    end
    fun get []
        return this :: n
    end
end

c1 = new Counter [0]
c2 = new Counter [100]
loop i in 0..5
    c1 :: increment []
end
assert c1 :: get [] == 5
assert c2 :: get [] == 100

# ── 4. Repeated creation of inherited classes in a loop ──
class Animal [name]
    fun speak []
        return this :: name
    end
end
class Dog [name] : Animal [name]
end

dogs = {}
loop i in 0..3
    d = new Dog ["dog_" + i]
    dogs << d
end
assert dogs{0} :: speak [] == "dog_0"
assert dogs{1} :: speak [] == "dog_1"
assert dogs{2} :: speak [] == "dog_2"

# ── 5. Deep inheritance chain, multiple instances ──
class Base [a]
end
class Mid [a, b] : Base [a]
end
class Leaf [a, b, c] : Mid [a, b]
end

leaves = {}
loop i in 0..3
    obj = new Leaf [i, i + 10, i + 100]
    leaves << obj
end

assert leaves{0} :: a == 0
assert leaves{0} :: b == 10
assert leaves{0} :: c == 100
assert leaves{2} :: a == 2
assert leaves{2} :: b == 12
assert leaves{2} :: c == 102

# ── 6. Cast works correctly across multiple instances ──
class User [email]
end
class Person [name]
end
class Student [email, name] : User [email], Person [name]
end

students = {}
loop i in 0..3
    s = new Student ["user_" + i + "@test.com", "student_" + i]
    students << s
end

assert students{0} as User :: email == "user_0@test.com"
assert students{0} as Person :: name == "student_0"
assert students{2} as User :: email == "user_2@test.com"
assert students{2} as Person :: name == "student_2"

# ── 7. Instance-of checks across multiple instances ──
assert students{0} is Student
assert students{0} is User
assert students{0} is Person
assert students{1} is Student

# ── 8. Mutating one instance's base property doesn't affect others ──
students{0} as Person :: name = "changed"
assert students{0} as Person :: name == "changed"
assert students{1} as Person :: name == "student_1"

# ── 9. Class with constructor body, created in a loop ──
class Pair [first, second]
    sum = first + second
end

pairs = {}
loop i in 0..4
    pairs << new Pair [i, i * 2]
end

assert pairs{0} :: sum == 0
assert pairs{1} :: sum == 3
assert pairs{3} :: sum == 9

# ── 10. Creating different classes alternately in a loop ──
results = {}
loop i in 0..3
    results << new Point [i, 0]
    results << new Counter [i]
end
assert results{0} :: x == 0
assert results{1} :: get [] == 0
assert results{2} :: x == 1
assert results{3} :: get [] == 1
assert results{4} :: x == 2
assert results{5} :: get [] == 2

print "all class tests passed"