

CSCS Control Flow Functions

| **CSCS Statement/Function**                  | **Description**                                     |
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
| **class** *className { }*                                    | A definition of a new class. it can optionaly inherit from one or more classes;    |
| **new** *className*                                  | Creates and returns an instance (object) of class className.    |
| **type** *(variableName)*                              | Returns type of the passed variable (e.g. NONE, STRING, NUMBER, ARRAY, OBJECT)|
| **true**                                   | A synonym for 1.    |
| **false**                                   | A synonym for 0.    |

        
