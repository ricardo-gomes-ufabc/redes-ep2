# Para realizar a build dos executáveis, rode um dos seguintes commandos:

## Compilar ambos os executáveis

```
dotnet build -c Sender_Release --no-incremental; dotnet build -c Receiver_Release --no-incremental
```

### Observação:
O projeto deve ser compilado utilizando .NET 8


## Extra

### Sender:

#### Modo Debug:
```
dotnet build -c Sender_Debug
```

#### Modo Release:
```
dotnet build -c Sender_Release
```

### Receiver:

#### Modo Debug:
```
dotnet build -c Receiver_Debug
```

#### Modo Release:
```
dotnet build -c Receiver_Release
```