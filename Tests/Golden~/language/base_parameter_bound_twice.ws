# Q-16: an instance has one field per name (5.7), so two base classes may
# not bind parameters of the same name to different arguments. Where the
# base classes are known, it is a compile error.
class Sub [a, b]
end

class Mul [a, b]
end

class Calculator [p, q] : Sub [q, p], Mul [p, q]
end
# expect-compile-error: 'a' is bound twice in class 'Calculator': base class 'Mul' binds its parameter 'a' to a different argument
