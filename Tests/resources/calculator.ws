# Each base class names its parameters differently (Q-16): an instance has
# one field per name, so two bases may not bind a parameter of the same name
# to different arguments (Sub [q, p] and Mul [p, q] binding 'a' both ways).
class Add [x, y]
    fun sum
        return @x + @y
    end
end

class Mul [ma, mb]
    fun mul
        return @ma * @mb
    end
end

class Sub [sa, sb]
    fun sub
        return @sa - @sb
    end
end

class Div [dm, dn]
    fun div
        return @dm / @dn
    end
end

class Exp [em, en]
    fun exp
        return pow[@em, @en]
    end
end

class Fib [ fn ]
    fun fib
        return fib [ @fn ]
    end

    fun fib [ n ]
        if n < 2
            return n
        end
        return fib [ n - 1 ] + fib [ n - 2 ]
    end
end

class Calculator [p, q]: Add [p, q], Sub [q, p],
                         Mul [p, q], Div [q, p],
                         Exp [p, q], Fib [ q ]
end

calc = new Calculator [2, 10]
assert calc :: sum [] == 12
assert calc :: sub [] == 8
assert calc :: mul [] == 20
assert calc :: div [] == 5
assert round[calc :: exp []] == 1024
assert calc :: fib [] == 55