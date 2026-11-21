# T3.4 (5.7, PROP-10, PROP-20, SEM-4): in a class body a bare read finds a
# field first, then a global; a bare assignment declares a field, even when
# a global has that name, unless the name is declared global; an assignment
# in a block of the class body declares a field too (DEC-4).
g = 5
hp = 1000
made = 0

class Unit [hp]
    global made
    seen = g            # the global
    doubled = hp * 2    # the field (constructor parameter), not the global
    if hp > 10
        big = "yes"     # a field
    end
    made = made + 1     # the global
    fun show []
        return "{@seen} {@doubled} {@big}"
    end
end

u = new Unit [20]
print u :: show []
print u :: big
print hp
print made
v = new Unit [3]
print made
# expect: 5 40 yes
# expect: yes
# expect: 1000
# expect: 1
# expect: 2
