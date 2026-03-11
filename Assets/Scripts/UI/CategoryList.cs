using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class CategoryList : MonoBehaviour
{
    [SerializeField] CategoryTab[] categories;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        foreach(var (category, i) in categories.Select((value, index) => (value, index)))
        {
            category.button.onClick.AddListener(() => Select(i));
            category.Initialize();
        }

        categories[0].OnClicked();
    }


    void Select(int index)
    {
        foreach (var category in categories)
        {
            if(category != categories[index])
            {
                category.isSelected = false;
            } else
            {
                category.isSelected = true;
            }
        }
    }
}
