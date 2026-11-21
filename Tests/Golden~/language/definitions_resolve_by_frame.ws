# T3.5: where a call by name and a `new` look up their function or class
# depends on the frame, not on a class scope pushed on the definition stack:
# a method or constructor frame uses its class's definitions, a function or
# lambda frame its caller's, the top level the script's.
fun label [] return "top-level label" end
fun ask_label [] return label [] end

class Squad [name]
    class Member [n]
        fun describe [] return "member {@n}" end
    end
    fun label [] return "squad label" end
    fun via_method [] return label [] end
    fun via_function [] return ask_label [] end
    fun via_tail [] return via_method [] end
    fun recruit [n]
        m = new Member [n]
        return m :: describe []
    end
    first = recruit [1]
end

s = new Squad ["red"]
print s :: via_method []
# A function called from a method takes the method's definitions (as when the
# class scope was pushed for the method and every call it made).
print s :: via_function []
print s :: via_tail []
print s :: recruit [2]
print s :: first
print label []
print ask_label []

# A method that raises leaves no class definitions behind.
class Faulty []
    fun label [] return "faulty label" end
    fun boom [] return 1 / 0 end
end
f = new Faulty
begin
    f :: boom []
rescue e
    print "rescued"
end
print label []
# expect: squad label
# expect: squad label
# expect: squad label
# expect: member 2
# expect: member 1
# expect: top-level label
# expect: top-level label
# expect: rescued
# expect: top-level label
