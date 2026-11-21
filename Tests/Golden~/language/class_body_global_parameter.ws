# T3.4 (PROP-10): a constructor parameter is a field, so it cannot be
# declared global in the class body.
class Unit [hp]
    global hp   # expect-compile-error: 'hp' is a constructor parameter and cannot be declared global
end
