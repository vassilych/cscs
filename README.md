

CSCS Control Flow Functions

| **CSCS Statement**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **include**(*pathToFile*)                    | Include another scripting file, e.g. include("functions.cscs");   
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
| **throw** *string;*                                    | Throw an exception, e.g. throw "value must be positive";    |
| **true**                                   | Represents a boolean value of true. Equivalent to number 1.    |
| **false**                                   | Represents a boolean value of false. Equivalent to number 0.    |



CSCS Object-Oriented Functons

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **class** *className : Class1, Class2, ... { }*     | A definition of a new class. it can optionaly inherit from one or more classes. Inside of a class definition you can have constructors, functions, and variable definitions. You access these variables and functions using the dot notation (all of them are public).|
| **new** *className(param1, param2, ...)*  | Creates and returns an instance (object) of class className. There can be a zero or more parameters passed to the class constructor (depending on the class constructer prameter definitions).|
| **properties** | object.properties returns a list of all properties that this object implements. For each of these properties is legal to call object.property.|
| **GetProperty** (*objectName, propertyName*)  | Returns objectName.propertyName.|
| **GetPropertyStrings** *(objectName)*  | Same as calling objectName.properties.|
| **SetProperty** (*objectName, propertyName, propertyValue*)  | Same as objectName.propertyName = propertyValue.|



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



CSCS Variable and Array Functions

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **Add**(*array, value, index = -1*)  | Appends value to the current variable array. If index is greater or equal to zero, inserts it at the index.|
| **AddVariableToHash** (*array, value, hashKey*)                    | Appends a value to the list of values of a given hash key.|
| **AddAllToHash** (*array, values, startFrom, hashKey, sep = "\t"*)  | Add all of the values in values list to the hash map variable. E.g. AddAllToHash("categories", lines, startWords, "all");  |
| **Contains**(*variable, value*)    | Checks if the current variable contains another variable. Makes sense only if curent variable is an array.|
| **DeepCopy** (*variable*)    | Makes a deep copy of the passed object, assigning new memory to all of it array members.|
| **DefineLocal** (*variable, value=""*)    | Defines a variable in local scope. Makes sense only if a global variable with this name already exists (without this function, a global variable will be used and modified).|
| **FindIndex** (*array, value*)    | Looks for the value in the specified array and return its index if found, or -1 otherwise.|
| **Remove** (*array, value*)    | Removes specified value from the array. Returns true on success and false otherwise.|
| **RemoveAt** (*array, index*)    | Removes a value from the array at specified index. Returns true on success and false otherwise.|
| **Size** (*array*)           | Returns number of elements in an array (for strings returns length of the string). |
| **Type** *(variableName)*             | Returns type of the passed variable (e.g. NONE, STRING, NUMBER, ARRAY, OBJECT).|



CSCS Conversion Functions

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **Bool** (*variable*)  | Converts variable to a Boolean value.|
| **Decimal** (*variable*)  | Converts variable to a decimal value.|
| **Double** (*variable*)  | Converts variable to a double value.|
| **Int** (*variable*)  | Converts variable to an integer value.|
| **String** (*variable*)  | Converts variable to a string value.|



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



CSCS Debugger

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **StartDebugger** ()          | Starts running a debugger server (to accept connections from Visual Studio Code).|
| **StopDebugger** ()          | Stops running a debugger server.|



CSCS Miscelaneous Functions

| **CSCS Function**                  | **Description**                                     |
| :------------------------------------------- |:------------------------------------------------|
| **Env** (*variableName*)                   | Returns value of the passed environment variable  |
| **Exit** (*code = 0*)               | Stops execution and exits with passed return code.   |
| **Lock** { *statements;* }          | Uses a global lock object to lock the execution of code in curly braces.  |
| **Now** (*format="HH:mm:ss.fff"*)          | Returns current date and time according to the specified format. |
| **Print** (*var1="", var2="", ...*)          | Prints specified parameters, converting them all to strings. |
| **PsTime**       | Returns current process CPU time. Used for measuring the script execution time. |
| **Show** (*funcName*)          | Prints contents of a CSCS function. |
| **Signal** ()         | Signals waiting threads. |
| **Sleep** (*millisecs*)          | Sleeps specified number of milliseconds. |
| **Thread** (*functionName*) OR { *statements;* } | Starts a new thread. The thread will either execute a specified CSCS function or all the statements between the curly brackets. |
| **ThreadId** () | Returns current thread Id. |
| **Wait** ()         | Waits for a signal.  | 


