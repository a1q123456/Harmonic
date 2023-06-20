using Harmonic.Controllers;
using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Rtmp;
using Harmonic.Networking.Rtmp.Messages.Commands;
using Harmonic.Networking.Rtmp.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Harmonic.Rpc;

internal class RpcParameter
{
    public bool IsOptional;
    public bool IsFromCommandObject;
    public bool IsCommandObject;
    public bool IsFromOptionalArgument;
    public int OptionalArgumentIndex;
    public string CommandObjectKey;
    public Type ParameterType;
}

internal class RpcMethod
{
    public string MethodName;
    public MethodInfo Method;

    public List<RpcParameter> Parameters = new();
}

internal class RpcService
{
    public Dictionary<Type, List<RpcMethod>> Controllers = new();

    public void PrepareMethod<T>(T instance, CommandMessage command, out MethodInfo methodInfo, out object[] callArguments) where T: RtmpController
    {
        if (!Controllers.TryGetValue(instance.GetType(), out var methods))
        {
            throw new EntryPointNotFoundException();
        }
            
        foreach (var method in methods)
        {
            if (method.MethodName != command.ProcedureName)
            {
                continue;
            }
            var arguments = new object[method.Parameters.Count];
            var i = 0;
            foreach (var para in method.Parameters)
            {
                if (para.IsCommandObject)
                {
                    arguments[i] = command.CommandObject;
                    i++;
                }
                else if (para.IsFromCommandObject)
                {
                    var commandObj = command.CommandObject;
                    object val = null;
                    if (!commandObj.Fields.TryGetValue(para.CommandObjectKey, out val) && !commandObj.DynamicFields.TryGetValue(para.CommandObjectKey, out val))
                    {
                        if (para.IsOptional)
                        {
                            arguments[i] = Type.Missing;
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (para.ParameterType.IsAssignableFrom(val.GetType()))
                    {
                        arguments[i] = val;
                        i++;
                    }
                    else
                    {
                        if (para.IsOptional)
                        {
                            arguments[i] = Type.Missing;
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else if (para.IsFromOptionalArgument)
                {
                    var optionArguments = command.GetType().GetProperties().Where(p => p.GetCustomAttribute<OptionalArgumentAttribute>() != null).ToList();
                    if (para.OptionalArgumentIndex >= optionArguments.Count)
                    {
                        if (para.IsOptional)
                        {
                            arguments[i] = Type.Missing;
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        arguments[i] = optionArguments[i].GetValue(command);
                        i++;
                    }
                }
            }

            if (i == arguments.Length)
            {
                methodInfo = method.Method;
                callArguments = arguments;
                return;
            }
        }

        throw new EntryPointNotFoundException();
    }

    internal void CleanupRegistration()
    {
        foreach (var controller in Controllers)
        {
            var gps = controller.Value.GroupBy(m => m.MethodName).Where(gp => gp.Count() > 1);

            var hiddenMethods = new List<RpcMethod>();

            foreach (var gp in gps)
            {
                hiddenMethods.AddRange(gp.Where(m => m.Method.DeclaringType != controller.Key));
            }
            foreach (var m in hiddenMethods)
            {
                controller.Value.Remove(m);
            }
        }
    }

    internal void RegeisterController(Type controllerType)
    {
        var methods = controllerType.GetMethods();
        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<RpcMethodAttribute>();
            if (attr != null)
            {
                var rpcMethod = new RpcMethod();
                var methodName = attr.Name ?? method.Name;
                var parameters = method.GetParameters();
                bool canInvoke = false;
                var optArgIndex = 0;
                foreach (var para in parameters)
                {
                    var fromCommandObject = para.GetCustomAttribute<FromCommandObjectAttribute>();
                    var fromOptionalArg = para.GetCustomAttribute<FromOptionalArgumentAttribute>();
                    var commandObject = para.GetCustomAttribute<CommandObjectAttribute>();
                    if (fromCommandObject == null && fromOptionalArg == null && commandObject == null)
                    {
                        break;
                    }
                    canInvoke = true;
                    if (fromCommandObject != null)
                    {
                        var name = fromCommandObject.Key ?? para.Name;
                        rpcMethod.Parameters.Add(new RpcParameter()
                        {
                            CommandObjectKey = name,
                            IsFromCommandObject = true,
                            ParameterType = para.ParameterType,
                            IsOptional = para.IsOptional
                        });
                    }
                    else if (fromOptionalArg != null)
                    {
                        rpcMethod.Parameters.Add(new RpcParameter()
                        {
                            OptionalArgumentIndex = optArgIndex,
                            IsFromOptionalArgument = true,
                            ParameterType = para.ParameterType,
                            IsOptional = para.IsOptional
                        });
                        optArgIndex++;
                    }
                    else if (commandObject != null && para.ParameterType.IsAssignableFrom(typeof(AmfObject)))
                    {
                        rpcMethod.Parameters.Add(new RpcParameter()
                        {
                            IsCommandObject = true,
                            IsOptional = para.IsOptional
                        });
                    }
                }
                if (canInvoke || !parameters.Any())
                {
                    rpcMethod.Method = method;
                    rpcMethod.MethodName = methodName;
                    if (!Controllers.TryGetValue(controllerType, out var mapping))
                    {
                        Controllers.Add(controllerType, new List<RpcMethod>());
                    }
                    Controllers[controllerType].Add(rpcMethod);
                }
            }
        }
    }
}