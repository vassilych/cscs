CSCS (Customized Scripting in C#) is a scripting language, which is very easy to integrate into any C# project and adjust according to your needs. Basically, the concept of CSCS is not only a language, but also a framework that you can use to create your own language. Since the compiler will be inside of your project you can do with the language whatever you want: add new features, modify existing ones, etc. How to do that and the CSCS Framework itself have been described in:

* [Customized Scripting in C#](https://msdn.microsoft.com/en-us/magazine/mt632273.aspx),  MSDN
* [Programming your own language in C#](http://www.codemag.com/Article/1607081),  CODE Magazine
* [Implementing a Custom Language Succinctly](https://www.syncfusion.com/resources/techportal/details/ebooks/implementing-a-custom-language),  Syncfusion E-book

The source code for Mobile App development is [here](https://github.com/vassilych/mobile). The usage of CSCS in Mobile App development has been described in:

* [Developing Cross-Platform Native Apps with a Functional Language](http://www.codemag.com/article/1711081),  CODE Magazine
* [Writing Native Mobile Apps Using a Customizable Scripting Language](https://msdn.microsoft.com/en-us/magazine/mt829272),  MSDN
* [Writing Native Mobile Apps in a Functional Language Succinctly](https://www.syncfusion.com/ebooks/writing_native_mobile_apps_in_a_functional_language_succinctly),  Syncfusion E-book

The usage of CSCS in Unity has been described in:

* [Using Custom Scripting and Modding in Unity Game and App Development](https://www.codemag.com/Article/1903081),  CODE Magazine

The Visual Studio Code Extension to debug CSCS code is available [here](https://marketplace.visualstudio.com/items?itemName=vassilik.cscs-debugger).

<br>

Description of CSCS
======

* The syntax is a mixture between C#, JavaScript, and Python.
* All statements must end with a semicolon ";".
* Identation and new lines are not used in parsing (unlike Python).
* All CSCS variables have at least 3 properties that can be accessed using the dot notation: properties, type, size, and string.
* Variables and arrays are all defined implicitly, e.g. x=5, b[7]=11<br>
  An example of a list initialization: c = {"aa", "bb", "xxx"};<br>
  You can also define it explicitely: c[0]="aa"; c[1]="bb"; <br>
  Definition in index form doesn't have to start from index 0, or even from the first dimension: not defined elements will have a type NONE.
  E.g.: b[5][3][5][3]=15;<br>
  Similarly, when defining dictionaries, e.g.: x["bla"]["blu"]="wichtig";
* Control flow statements if, else, while, for, try, etc., all require statements between the curly braces (even for a single statement).
* "elif" means "else if" (like in Python)

<br>
What follows is the description of the CSCS functions. The usage of CSCS has been tested on Windows, Mac, iOS, Android, and Unity. Not all of the functions are supported on all of platforms. First, we see the core fuctions, that are supported everywhere, and then the extended functions, dealing more with OS internals and therefore not supported on all the platforms. 
<br>

CSCS Control Flow Functions
------

| **CSCS Statement**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **include** (*pathToFile*)                    | Includes another scripting file, e.g. include("functions.cscs");   
| **function** *funcName* (*param1*, *param2=value2*, *param3=value3*) { *statements;* } | Declares a custom function with 0 or more parameters. Parameters can optionally have default values. When calling a function, parameters can be specified either implicitly (e.g. sine(10)), or explicitely (e.g. func(param2=value2, param1=value1)).  |
| **cfunction** *funcName* (*param1*, *param2=value2*, *param3=value3*) { *statements;* } | Declares a custom precomplied function with 0 or more parameters. Doesn't work on iOS and Android.  |
| **return** or **return** *variable*;      | Finishes execution of a function and optionally can return a value.|
| **while** (*condition*) { *statements;* } | Execute loop as long as the condition is true. <br><b>Curly brackets are mandatory.</b>|
| **for** (*init*; *condition*; *step*) { *statements;* }  | A canonic for loop, e.g. for (i = 0; i < 10; ++i).<br><b>Curly brackets are mandatory.</b>|
| **for** (*item in listOfValues*) { *statements;* }  | Executes loop for each elemеnt of listOfValues.<br><b>Curly brackets are mandatory.</b>|
| **break**                                    | Breaks out of a loop.                  |
| **continue**                                 | Forces the next iteration of the loop. |
| **if** (*condition*) { *statements;* } <br> **elif** (*condition*) { *statements;* } <br> **else** { *statements;* } |If-else control flow statements.<br><b>Curly brackets are mandatory.</b>|
| **try** { *statements;* } <br> **catch**(*exceptionString*)  { *statements;* } | Try and catch control flow.<br><b>Curly brackets are mandatory.</b>|
| **throw** *string*;                  | Throws an exception, e.g. throw "value must be positive"; |
| **true**                             | Represents a boolean value of true. Equivalent to number 1.|
| **false**                            | Represents a boolean value of false. Equivalent to number 0.|

<br>

### Control Flow Example
<pre><code>include("functions.cscs");
i = 0;
for (i = 0; i < 13; i++) {
  b += (i*4 - 1);
  if ( i == 3) {
    break;
  } else {
    continue;
  }
  print("this is never reached");
}

a = 23; b = 22;
cond = "na";
if (a < b) {
  if (b < 15) {
    cond = "cond1";
  }
  elif  (b < 50) {
    cond = "cond2";
  }
}
elif (a >= 25) {
  cond = "cond3";
}
else {
  cond = "cond4";
}

function myp(par1, par2, par3 = 100) {
  return par1 + par2 + par3;
}
</code></pre>

<br>

### Functions and Try/Catch Example
<pre><code>function myp(par1, par2, par3 = 100) {
  return par1 + par2 + par3;
}

z = myp(par2=20, par1=70); // z = 190

try {
  z = myp(par2=20);
  print("Error. Missing Exception: Function [myp] arguments mismatch: 3 declared, 1 supplied.");
} catch(exc) {
  print("OK. Caught: " + exc);
}
try {
  z = myp(par2=20, par3=70);
  print("Error. Missing Exception: No argument [par1] given for function [myp].");
} catch(exc) {
  print("OK. Caught: " + exc);
}
</code></pre>

<br>

CSCS Object-Oriented Functions and Named Properties
------

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **class** *className : Class1, Class2, ... { }*     | A definition of a new class. it can optionaly inherit from one or more classes. Inside of a class definition you can have constructors, functions, and variable definitions. You access these variables and functions using the dot notation (all of them are public).|
| **new** *className(param1, param2, ...)*  | Creates and returns an instance (object) of class className. There can be a zero or more parameters passed to the class constructor (depending on the class constructer prameter definitions).|
| *variable*.**Properties** | Returns a list of all properties that this variable implements. For each of these properties is legal to call variable.property. Each variable implements at least the following properties: Size, Type, and Properties. |
| *variable*.**Size** | Returns either a number of elements in an array if variable is of type ARRAY or a number of characters in a string representation of this variable. |
| *variable*.**Type** | Returns this variable's type (e.g. NONE, STRING, NUMBER, ARRAY, OBJECT). |
| *variable*.**Contains(value)** | If variable is a list, whether it contains this value.|
| *variable*.**StartsWith(value)** | Whether the variable, converted to a string, starts with this value.|
| *variable*.**EndsWith(value)** | Whether the variable, converted to a string, ends with this value.|
| *variable*.**Replace(oldValue, newValue)** | Replaces oldValue with the newValue.|
| *variable*.**IndexOf(value, from)** | Returns index of the value in the variable (-1 if not found).|
| *variable*.**Join(sep=" ")** | Converts a list to a string, based on the string separation token.|
| *variable*.**First** | Returns the first character or first element of this string or list.|
| *variable*.**Last**  | Returns the last character or last element of this string or list. |
| *variable*.**Keys** | If the underlying variable is a dictionary, returns all the dictionary keys.|
| *variable*.**Substring(value, from, size)** | Returns a substring of a given string.|
| *variable*.**Tokenize(sep=" ")** | Returns a new list based on the string separation token.|
| *variable*.**Trim()** | Returns a new variable without leading or trailing white spaces.|
| *variable*.**Lower()** | Returns a variable converted to the lower case.|
| *variable*.**Upper()** | Returns a variable converted to the upper case.|
| *variable*.**Sort()** | Sorts underlying array.|
| *variable*.**Reverse()** | Reverses the contents of the underlying array or string.|
| **GetProperty** (*objectName, propertyName*)  | Returns variable.propertyName.|
| **GetPropertyStrings** (*objectName*)  | Same as calling variable.properties.|
| **SetProperty** (*objectName, propertyName, propertyValue*)  | Same as variable.propertyName = propertyValue.|

<br>

### Object-Oriented Example with Multiple Inheritance
<pre><code>class Stuff1 {
  x = 2;
  Stuff1(a) {
    x = a;
  } 
  function addStuff1(n) {
    return n + x;
  }
}

class Stuff2 {
  y = 3;
  Stuff2(b) {
    y = b;
  } 
  function addStuff2(n) {
    return n + y;
  }
}

class CoolStuff : Stuff1, Stuff2 {
  z = 3;
  CoolStuff(a, b, c) {
    x = a;
    y = b;
    z = c;
  } 
  function addCoolStuff() {
    return x + addStuff2(z);
  }
}

addition = 100;
obj1 = new Stuff1(10);
print(obj1.x); // prints 10
print(obj1.addStuff1(addition); // prints 110

obj2 = new Stuff2(20);
print(obj2.y); // prints 20 
print(obj2.addStuff2(addition)); // prints 120

newObj = new CoolStuff(11, 13, 17);
print(newObj.addCoolStuff()); // prints 41
print(newObj.addStuff1(addition)); // prints 111
print(newObj.addStuff2(addition)); // prints 113
</code></pre>

### Object-Oriented Example with a C# Compiled Object
<pre><code>ct = new CompiledTest();
ct.NaMe="Lala";
print(ct.name); // prints "Lala": properties are case-insensitive

ct.Extra = "New property";
props = ct.properties;
print(props.contains("Extra")); // prints 1 (true)
</code></pre>

CSCS Math Functions
------

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **Abs** (*value*)                   | Returns absolute value   
| **Acos** (*value*)                  | Returns arccosine function   
| **Asin** (*value*)                  | Returns arcsine function   
| **Ceil** (*value*)                  | Returns the smallest integral value which is greater than or equal to the specified decimal value
| **Cos** (*value*)                   | Cosine function   
| **Exp** (*value*)                   | Returns the constant e (2.718281828...) to the power of the specified value   
| **Floor** (*value*)                 | Returns the largest integral value less than or equal to the specified decimal value
| **GetRandom** (*limit, numberOfRandoms=1*)        | If numberOfRandoms = 1, returns a random variable between 0 and limit. Otherwise returns a list of numberOfRandoms integers, where each element is a random number between 0 and limit. If limit >= numberOfRandoms, each number will be present at most once|
| **Log** (*base, power*)             | Returns the natural logarithm of a specified number.
| **Pi**                              | Returns the constant pi (3.14159265358979...)
| **Pow** (*base, power*)             | Returns base to the specified power.
| **Round** (*number, digits=0*)      | Rounds a number according to the specified number of digits.
| **Sin** (*value*)                   | Sine function   
| **Sqrt** (*number*)                 | Returns the squared root of the specified number.

<br>

CSCS Variable and Array Functions
------

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **Add**(*variable, value, index = -1*)  | Appends value to the current variable array. If index is greater or equal to zero, inserts it at the index.|
| **AddVariableToHash** (*variable, value, hashKey*)                    | Appends a value to the list of values of a given hash key.|
| **AddAllToHash** (*variable, values, startFrom, hashKey, sep = "\t"*)  | Add all of the values in values list to the hash map variable. E.g. AddAllToHash("categories", lines, startWords, "all");  |
| **Contains** (*variable, value*)    | Checks if the current variable contains another variable. Makes sense only if curent variable is an array.|
| **DeepCopy** (*variable*)    | Makes a deep copy of the passed object, assigning new memory to all of it array members.|
| **DefineLocal** (*variable, value=""*)    | Defines a variable in local scope. Makes sense only if a global variable with this name already exists (without this function, a global variable will be used and modified).|
| **FindIndex** (*variable, value*)    | Looks for the value in the specified variable array and returns its index if found, or -1 otherwise.|
| **GetColumn** (*variable, column, fromRow=0*)    | Goes over all rows of the variable array starting from the specified row and return a specified column.|
| **GetKeys** (*variable*)    |If the underlying variable is a dictionary, returns all the dictionary keys.|
| **Remove** (*variable, value*)    | Removes specified value from the variable array. Returns true on success and false otherwise.|
| **RemoveAt** (*variable, index*)    | Removes a value from the variable array at specified index. Returns true on success and false otherwise.|
| **Size** (*variable*)           | Returns number of elements in a variable array or the length of the string (same as variable.Size). |
| **Type** *(variableName)*       | Returns type of the passed variable (same as variable.Type).|


### Array Example

<pre><code>a[1]=1; a[2]=2;
c=a[1]+a[2];

a[1][2]=22;
a[5][3]=15;
a[1][2]-=100;
a[5][3]+=100;

print(a[5][2]);

a[1][2]++;
print(a[1][2]);
print(a[5][3]++);
print(++a[5][3]);
print(--a[5][3]);
print(a[5][3]--);

b[5][3][5][3]=15;
print(++b[5][3][5][3]);

x["bla"]["blu"]=113;
x["bla"]["blu"]++;
x["blabla"]["blablu"]=126;
--x["blabla"]["blablu"];
</code></pre>

<br>

CSCS Conversion Functions
------

| **CSCS Function**        | **Description**                                 |
| :------------------------|:------------------------------------------------|
| **Bool** (*variable*)    | Converts a variable to a Boolean value.|
| **Decimal** (*variable*) | Converts a variable to a decimal value.|
| **Double** (*variable*)  | Converts a variable to a double value.|
| **Int** (*variable*)     | Converts a variable to an integer value.|
| **String** (*variable*)  | Converts a variable to a string value.|

<br>

CSCS String Functions
------


| **CSCS Function**                  | **Description**                                 |
| :----------------------------------|:------------------------------------------------|
| **Size** (*variableName*)          | Returns length of the string (for arrays returns number of elemnts in an array). |
| **StrBetween** (*string, from, to*) | Returns a substring with characters between substrings from and to.  
| **StrBetweenAny** (*string, from, to*) | Returns a substring with characters between any of the cgars in from and any of the chars in to.|
| **StrContains** (*string, argument, case=case*) | Returns whether a string contains a specified substring. The case parameter can be either "case" (default) or "nocase. |
| **StrEndsWith** (*string, argument, case=case*) | Returns whether a string ends with a specified substring. |
| **StrEqual** (*string, argument, case=case*) |  Returns whether a string is equal to a specified string. |
| **StrIndexOf** (*string, substring, case=case*)   | Searches for index of a specified substring in a string. Returns -1 if substring is not found. |
| **StrLower** (*string*)   | Returns string in lower case. |
| **StrReplace** (*string, src, dst*)   | Replaces all occurunces of src with dst in string, returning a new string. |
| **StrStartsWith** (*string, argument, case=case*) | Returns whether a string starts with a specified substring. |
| **StrTrim** (*string*)    | Removes all leading and trailing white characters (tabs, spaces, etc.), returning a new string. |
| **StrUpper** (*string*)   | Returns string in upper case. |
| **Substring** (*string, from=0, length=StringLength*)   | Returns a substring of specified string starting from a specified index and of specified length. |
| **Tokenize** (*string, separator="\t", option=""*)   | Converts string to a list of tokens based on the specified token separator. If option="prev", will convert all empty tokens to their previous token values.|
| **TokenizeLines** (*newVariableName, variableWithLines, fromLine=0, separator="\t"*)   | Converts a list of string in variableWithLines to the list of tokens based on the specified token separator. Adds result to a new variable newVariableName.|

<br>

###  Measuring Execution Time, Throwing Exceptions, and String Manipulation Examples
<pre><code>cycles = 1000; i = 0;
start = PsTime();
while ( i++ < cycles) {
    str = " la la ";
    str = StrTrim(str);
    str = StrReplace(str, "la", "lu");
    if (str != "lu lu") {    
      throw "Wrong result: [" + str + "] instead of [lu lu]";
    }
}
end = PsTime();
print("Total CPU time of", cycles, "loops:", end-start, "ms.");
// Example output: Total CPU time of 1000 loops: 968.75 ms.
</code></pre>
<br>
<br>

CSCS Debugger
------

| **CSCS Function**                  | **Description**                                     |
| :----------------------------------|:------------------------------------------------|
| **StartDebugger** *(port=13337)*   | Starts running a debugger server on a specified port (to accept connections from Visual Studio Code).|
| **StopDebugger** ()          | Stops running a debugger server.|

<br>

CSCS Core Miscellaneous Functions
------

| **CSCS Function**                   | **Description**                                 |
| :-----------------------------------|:------------------------------------------------|
| **Env** (*variableName*)            | Returns value of the specified environment variable.|
| **Lock** { *statements;* }          | Uses a global lock object to lock the execution of code in curly braces.|
| **Now** (*format="HH:mm:ss.fff"*)   | Returns current date and time according to the specified format.|
| **Print** (*var1="", var2="", ...*) | Prints specified parameters, converting them all to strings. |
| **PsTime**                          | Returns current process CPU time. Used for measuring the script execution time. |
| **SetEnv** (*variableName, value*)  | Sets value of the specified environment variable.  |
| **Show** (*funcName*)               | Prints contents of a specified CSCS function. |
| **Singleton** (*code*)              | Creates a singleton Variable. The code is executed only once. See an example below.|
| **Signal** ()                       | Signals waiting threads. |
| **Sleep** (*millisecs*)             | Sleeps specified number of milliseconds.
| **Thread** (*functionName*) OR { *statements;* } | Starts a new thread. The thread will either execute a specified CSCS function or all the statements between the curly brackets. |
| **ThreadId** () | Returns current thread Id. |
| **Wait** ()         | Waits for a signal.  | 

<br>

###  Singleton Example
<pre><code>function CreateArray(size, initValue = 0) {
    result = {};
    for (i = 0; i < size; i++) {
        result[i] = initValue;
    }
    return result;
}

pattern = "CreateArray(5, 'Test')";
uniqueArray = Singleton(pattern);
uniqueArray[2] = "Extra";
arr = Singleton(pattern);
print("array=", arr); // {Test, Test, Extra, Test, Test}
</code></pre>
<br>

All of the functions above are supported on all devices. But there are also a few functions that have more access to the OS internals and are supported only for Windows or Mac apps. They are below.
<br>


CSCS File and Command-Line Functions (not available in Unity, iOS, Android)
------


| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **cd** *pathname*          | Changes current directory to pathname.|
| **cd..**              | Changes current directory to its parent (one level up).|
| **clr**           | Clear contents of the Console.|
| **copy** *source destination*          | Copies source to destination. Source can be a file, a directory or a pattern (like \*.txt).|
| **delete** *pathname*          | Deletes specified file or directory.|
| **dir** *pathname=currentDirectory*          | Lists contents of the specified directory.|
| **exists**  *pathname*         | Returns true if the specified pathname exists and false otherwise.|
| **findfiles**  *pattern1, pattern2="", ...*         | Searches for files with specified patterns.|
| **findstr** *string, pattern1, pattern2="", ...*         | Searches for a specified string in files with specified patterns.|
| **kill** *processId*         | Kills a process with specified Id.|
| **mkdir** *dirName*         | Creates a specified directory.|
| **more** *filename*         | Prints content of a file to the screen with the possibility to get to the next screen with a space.|
| **move** *source, destination*         | Moves source to destination. Source can be a file or a directory.|
| **printblack** (*arg1, arg2="", ...*)         | Prints specified arguments in black color on console.|
| **printgray** (*arg1, arg2="", ...*)         | Prints specified arguments in black color on console.|
| **printgreen** (*arg1, arg2="", ...*)         | Prints specified arguments in black color on console.|
| **printred** (*arg1, arg2="", ...*)         | Prints specified arguments in black color on console.|
| **psinfo** *pattern*         | Prints process info for all processes having name with the specified pattern.|
| **pwd**         | Prints current directory.|
| **read**          | Reads and returns a string from console.|
| **readfile** *filename*         | Reads a file and returns an array with its contents.|
| **readnum**         | Reads and returns a number from console.|
| **run** *program, arg1="", arg2=""...*         | Runs specified process with specified arguments.|
| **tail** *filename, numberOfLines=20*         | Prints last numberOfLines of a specified filename.|
| **writeline** *filename, line*         | Writes specified line to a file.|
| **writelines** *filename, variable*         | Writes all lines from a variable (which must be an array) to a file.|

<br>

CSCS Extended Miscellaneous Functions
------


| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **CallNative** (*methodName, parameterName, parameterValue*)    | Calls a C# static method, implemented in Statics.cs, from CSCS code, passing a specified parameter name and value. Not available on iOS and Android. |
| **Exit** (*code = 0*)               | Stops execution and exits with the specified return code.   |
| **GetNative** (*variableName*)    | Gets a value of a specified C# static variable, implemented in Statics.cs, from CSCS code. Not available on iOS and Android. |
| **SetNative** (*variableName, variableValue*)    | Sets a specified value to a specified C# static variable, implemented in Statics.cs, from CSCS code. Not available on iOS and Android. |
| **StartStopWatch** ()          | Starts a stopwatch. There is just one stopwatch in the system. |
| **StopStopWatch** ()          | Stops a stopwatch. There is just one stopwatch in the system. A format is either of this form: "hh::mm:ss.fff" or "secs" or "ms".|
| **StopWatchElapsed** (*format=secs*)          | Returns elapsed time according to the specified format. A format is either of this form: "hh::mm:ss.fff" or "secs" or "ms".|
| **Timestamp** (*doubleValue, format="yyyy/MM/dd HH:mm:ss.fff"*)   | Converts specified number of milliseconds since 01/01/1970 to a date time string according to the passed format. |

<br>

Extending CSCS with new Functions
------

To extend CSCS language with a new function, we need to perform two tasks. First, we define a new class, deriving from the ParserFunction class.

Second, we register the newly created class with the parser in an initialization phase as follows: 
  ParserFunction.RegisterFunction(FunctionName, FunctionImplementation);

Let's see an example how to do that with a random number generator function.
<br>

### A CSCS Function Implementing a Random Number Generator

<pre><code>class GetRandomFunction : ParserFunction
{
    static Random m_random = new Random();

    protected override Variable Evaluate(ParsingScript script)
    {
        // Extract all passed function args:
        List&lt;Variable&gt; args = script.GetFunctionArgs();

        // Check that we should have at least one argument:
        Utils.CheckArgs(args.Count, 1, m_name);

        // Check that the limit is a positive integer:
        Utils.CheckPosInt(args[0]);

        int limit = args[0].AsInt();
        return new Variable(m_random.Next(0, limit));
    }
}
</code></pre>

<br>



### Registering A CSCS Function with the Parser

<pre><code>ParserFunction.RegisterFunction("Random", new GetRandomFunction());</code></pre>


That's it! Now inside of CSCS we can just execute the following statement:

 <pre><code>x = Random(100);</code></pre>
 
 
 
 and x will get a random value between 0 and 100.
