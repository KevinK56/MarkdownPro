# Welcome to Markdown Pro

Built-in support for **Markdown** and **Mermaid** diagrams.

## 1. Example Mermaid Diagram
```mermaid
graph LR
    rawTextNode[Raw Text] --> markedNode(Marked.js)
    markedNode --> viewDecisionNode{Rendered View}
    viewDecisionNode -->|Success| successNode([Success])

    style rawTextNode fill:#e1f5fe,stroke:#03a9f4,stroke-width:2px
    style markedNode fill:#fff8e1,stroke:#ffb300,stroke-width:2px
    style viewDecisionNode fill:#ede7f6,stroke:#5e35b1,stroke-width:2px
    style successNode fill:#e8f5e9,stroke:#2e7d32,stroke-width:2px
```
## 2. Dynamic Tables


| Feature | Supported |
| :--- | :--- |
| Real-time | Yes |
| Export | Yes |
| Privacy | 100% |

## 3. Code Snippets

```sql
SELECT *
FROM dbo.CustomerDetails
WHERE Name Like 'Test'
ORDER BY ID DESC

```

```C#
using System;

namespace ClassExample
{
    // Defining the class
    public class Car
    {
        // Properties (Attributes)
        public string Brand { get; set; }
        public string Model { get; set; }
        public int Year { get; set; }

        // The Constructor method (runs automatically when an object is created)
        public Car(string brand, string model, int year)
        {
            Brand = brand;
            Model = model;
            Year = year;
        }

        // A method to display information
        public string DisplayInfo()
        {
            return $"{Year} {Brand} {Model}";
        }

        // A method representing an action
        public string StartEngine()
        {
            return $"The engine of the {Model} is now running.";
        }
    }
}
```




> Click **File > Export** to download this file as a PDF or Markdown document..
