# T3.10 (LANGUAGE.md section 5, plan 5.5): 'loop v in obj' iterates the
# constructor parameters of an instance, in order; body fields are not
# included. Anything but an array or an instance: errors/iterate_non_iterable.ws.
class Unit [name, hp]
    armor = 3
end
u = new Unit ["robin", 50]
loop v in u
    print v
end
# expect: robin
# expect: 50
