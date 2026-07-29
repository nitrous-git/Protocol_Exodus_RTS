using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    private float maxHealth;
    private float currentHealth;
    private bool initialized;

    public bool IsAlive => initialized && currentHealth > 0f;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float HealthRatio => maxHealth <= 0f ? 0f : currentHealth / maxHealth;

    public event Action OnDied;
    public event Action<float, float> OnHealthChanged;

    public void Initialize(float maxHealth)
    {
        this.maxHealth = Mathf.Max(1f, maxHealth);
        currentHealth = this.maxHealth;
        initialized = true;

        OnHealthChanged?.Invoke(currentHealth, this.maxHealth);
    }

    public void ApplyDamage(DamageInfo damageInfo)
    {
        if (!IsAlive)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - damageInfo.Amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0f)
            OnDied?.Invoke();
    }

    public void Heal(float amount)
    {
        if (!IsAlive)
            return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + Mathf.Max(0f, amount));
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }
}
