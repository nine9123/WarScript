# An error in a constructor body is raised at its line in the constructor's
# frame (plan T2.5).
class Unit [hp]
    ratio = 10 / hp   # expect-error: DivisionByZero
end
u = new Unit [0]
