
<br><br>
CSCS Control Flow Functions

| **CSCS Statement**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **include** (*pathToFile*)                    | Include another scripting file, e.g. include("functions.cscs");   
| **function** *funcName* (*param1*, *param2=value2*, *param3=value3*) { *statements;* } | Declare a custom function with 0 or more parameters. Parameters can optionally have default values. When calling a function, parameters can be specified either implicitely (e.g. sine(10)), or explicitely (e.g. func(param2=value2, param1=value1))  |
| **cfunction** *funcName* (*param1*, *param2=value2*, *param3=value3*) { *statements;* } | Declare a custom precomplied function with 0 or more parameters. Doesn't work on iOS and Android.  |
| **return** or **return** *variable*          | Finishes execution of a function and optionally can return a value                          |
| **while** (*condition*) { *statements;* }                                    | Execute loop as long as the condition is true, e.g. for (i = 0; i < 10; ++i).<br>Curly brackets are mandatory.   |
| **for** (*init*; *condition*; *step*) { *statements;* }  | A canonic for loop, e.g. for (i = 0; i < 10; ++i).<br>Curly brackets are mandatory.          |
| **for** (*item in listOfValues*) { *statements;* }  | Execute loop for each elemеnt of listOfValues.<br>Curly brackets are mandatory.          |
| **break**                                    | Breaks out of a loop                            |
| **continue**                                 | Forces the next iteration of the loop           |
| **if** (*condition*) { *statements;* } <br> **elif** (*condition*) { *statements;* } <br> **else** { *statements;* } |If-else control flow statements.<br>Curly brackets are mandatory.|
| **try** { *statements;* } <br> **catch**(*exceptionString*)  { *statements;* } | Try and catch control flow.<br>Curly brackets are mandatory.|
| **throw** *string*;                                    | Throw an exception, e.g. throw "value must be positive";    |
| **true**                                   | Represents a boolean value of true. Equivalent to number 1.    |
| **false**                                   | Represents a boolean value of false. Equivalent to number 0.    |

<br><br>
CSCS Object-Oriented Functions and named properties

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **class** *className : Class1, Class2, ... { }*     | A definition of a new class. it can optionaly inherit from one or more classes. Inside of a class definition you can have constructors, functions, and variable definitions. You access these variables and functions using the dot notation (all of them are public).|
| **new** *className(param1, param2, ...)*  | Creates and returns an instance (object) of class className. There can be a zero or more parameters passed to the class constructor (depending on the class constructer prameter definitions).|
| *variable*.**Properties** | Returns a list of all properties that this variable implements. For each of these properties is legal to call variable.property. Each variable implements at least the following properties: Size, Type, and Properties. |
| *variable*.**Size** | Returns either a number of elements in an array if variable is of type ARRAY or a number of characters in a string representation of this variable. |
| *variable*.**Type** | Returns this variable's type (e.g. NONE, STRING, NUMBER, ARRAY, OBJECT). |
| **GetProperty** (*objectName, propertyName*)  | Returns variable.propertyName.|
| **GetPropertyStrings** (*objectName*)  | Same as calling variable.properties.|
| **SetProperty** (*objectName, propertyName, propertyValue*)  | Same as variable.propertyName = propertyValue.|


<br><br>
CSCS Math Functions

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **Abs** (*value*)                   | Returns absolute value   
| **Acos** (*value*)                  | Returns arccosine function   
| **Asin** (*value*)                  | Returns arcsine function   
| **Ceil** (*value*)                  | Returns the smallest integral value which is greater than or equal to the specified decimal value
| **Cos** (*value*)                   | Cosine function   
| **Exp** (*value*)                   | Returns constant e (2.718281828...) to the power of the specified value   
| **Floor** (*value*)                 | Returns the largest integral value less than or equal to the specified decimal value
| **Log** (*base, power*)             | Returns the natural logarithm of a specified number.
| **Pi**                              | Returns pi constant (3.14159265358979...)
| **Pow** (*base, power*)             | Returns base to the specified power.
| **GetRandom** (*limit, numberOfRandoms=1*)        | If numberOfRandoms = 1, returns a random variable between 0 and limit. Otherwise returns a list of numberOfRandoms integers, where each element is a random number between 0 and limit. Id limit >= numberOfRandoms, each number will be present at most once|
| **Round** (*number, digits=0*)             | Rounds number according to the specified number of digits.
| **Sqrt** (*number*)             | Returns squeared root of the specified number.
| **Sin** (*value*)                   | Sine function   

<br><br>
CSCS Variable and Array Functions

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
| **Type** *(variableName)*             | Returns type of the passed variable (same as variable.Type).|

<br><br>
CSCS Conversion Functions

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **Bool** (*variable*)  | Converts variable to a Boolean value.|
| **Decimal** (*variable*)  | Converts variable to a decimal value.|
| **Double** (*variable*)  | Converts variable to a double value.|
| **Int** (*variable*)  | Converts variable to an integer value.|
| **String** (*variable*)  | Converts variable to a string value.|

<br><br>
CSCS String Functions

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **Size** (*variableName*)           | Returns length of the string (for arrays returns number of elemnts in an array). |
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

<br><br>
CSCS Debugger

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **StartDebugger** ()          | Starts running a debugger server (to accept connections from Visual Studio Code).|
| **StopDebugger** ()          | Stops running a debugger server.|


<br><br>
CSCS File and Command-Line Functions

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

<br><br>
CSCS Miscelaneous Functions

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **CallNative** (*methodName, parameterName, parameterValue*)    | Calls a C# static method, implemented in Statics.cs, from CSCS code, passing a specified parameter name and value. Not available on iOS and Android. |
| **Env** (*variableName*)                   | Returns value of the specified environment variable.  |
| **Exit** (*code = 0*)               | Stops execution and exits with the specified return code.   |
| **GetNative** (*variableName*)    | Gets a value of a specified C# static variable, implemented in Statics.cs, from CSCS code. Not available on iOS and Android. |
| **Lock** { *statements;* }          | Uses a global lock object to lock the execution of code in curly braces.  |
| **Now** (*format="HH:mm:ss.fff"*)          | Returns current date and time according to the specified format. |
| **Print** (*var1="", var2="", ...*)          | Prints specified parameters, converting them all to strings. |
| **PsTime**       | Returns current process CPU time. Used for measuring the script execution time. |
| **SetEnv** (*variableName, value*)                   | Sets value of the specified environment variable.  |
| **SetNative** (*variableName, variableValue*)    | Sets a specified value to a specified C# static variable, implemented in Statics.cs, from CSCS code. Not available on iOS and Android. |
| **Show** (*funcName*)          | Prints contents of a specified CSCS function. |
| **Signal** ()         | Signals waiting threads. |
| **Sleep** (*millisecs*)          | Sleeps specified number of milliseconds.
| **StartStopWatch** ()          | Starts a stopwatch. There is just one stopwatch in the system. |
| **StopStopWatch** ()          | Stops a stopwatch. There is just one stopwatch in the system. A format is either of this form: "hh::mm:ss.fff" or "secs" or "ms".|
| **StopWatchElapsed** (*format=secs*)          | Returns elapsed time according to the specified format. A format is either of this form: "hh::mm:ss.fff" or "secs" or "ms".|
| **Thread** (*functionName*) OR { *statements;* } | Starts a new thread. The thread will either execute a specified CSCS function or all the statements between the curly brackets. |
| **ThreadId** () | Returns current thread Id. |
| **Timestamp** (*doubleValue, format="yyyy/MM/dd HH:mm:ss.fff"*)   | Converts specified number of milliseconds since 01/01/1970 to a date time string according to the passed format. |
| **Wait** ()         | Waits for a signal.  | 


