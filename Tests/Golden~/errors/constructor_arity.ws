# 'new' with another number of arguments than the class's parameters is an
# ArgumentError (PROP-29): no argument is dropped or made up. The compiler
# reports it when no class of that name takes that many
# (language/new_wrong_arity.ws); here another nested class 'Part' does.
class Car
    class Part [name]
    end
end
class Boat
    class Part
    end
end
boat = new Boat []
p = boat :: new Part ["sail"]   # expect-error: ArgumentError: Class 'Part' takes 0 arguments, not 1
