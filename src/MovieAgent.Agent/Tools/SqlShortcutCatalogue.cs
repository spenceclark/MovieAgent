namespace MovieAgent.Agent.Tools;

/// <summary>
/// The two tools of the <c>sql-shortcut</c> control surface: everything the main catalogue
/// exists to forbid, in one place.
/// </summary>
/// <remarks>
/// <b>This is a control, not a capability.</b> The whole premise of the main sweep is that a
/// generic SQL surface destroys what is being measured — a five-hop dependency chain becomes one
/// join. That premise was asserted rather than tested, and this surface tests it: give a model
/// the shortcut and see which failures survive. It separates "cannot emit a tool call at all"
/// from "cannot compose a chain across turns".
/// <para>
/// <b>Read this before comparing scores.</b> Text-to-SQL is a far better represented capability
/// than agentic tool composition, with vastly more training data behind it. A model that improves
/// on this surface is evidence that <em>the task changed</em>, not that the model is a better
/// agent. The delta is the finding; the absolute number is not a ranking.
/// </para>
/// <para>
/// Kept deliberately out of <see cref="ToolCatalogue.All"/> so
/// <see cref="ToolCatalogueValidator"/> continues to prove the main catalogue is join-free and
/// one-table-per-tool. The validator additionally rejects any non-descriptor tool that finds its
/// way into the main catalogue, so this separation cannot rot.
/// </para>
/// </remarks>
public static class SqlShortcutCatalogue
{
    /// <summary>Same row cap as the standard surface, so output shape is not a second variable.</summary>
    public const int MaxRows = 20;

    /// <summary>
    /// A compact column listing for the tables the standard surface covers.
    /// </summary>
    /// <remarks>
    /// Generated from the live Pagila database and pasted here as a constant rather than read at
    /// run time on purpose: <c>information_schema</c> and <c>pg_catalog</c> are on the banned list
    /// in <see cref="ToolCatalogueValidator.BannedObjects"/>, and the ban applies to the harness's
    /// own queries as much as to the model's. A static string keeps the shortcut surface honest
    /// about exactly what it hands over, and keeps it deterministic.
    /// <para>
    /// The trailing note names the relationships Postgres does not declare as foreign keys.
    /// That is parity, not generosity: the standard surface already tells the model the same
    /// thing in prose — <c>get_store</c>'s description reads "Returns manager_staff_id and
    /// address_id as numbers; use get_staff and get_address to resolve them." Withholding it here
    /// would make the shortcut surface harder than the surface it is being compared against, on a
    /// point unrelated to the thing being measured.
    /// </para>
    /// </remarks>
    public const string SchemaListing = """
        actor(actor_id PK, first_name, last_name, last_update)
        address(address_id PK, address, address2, district, city_id FK->city, postal_code, phone, last_update)
        category(category_id PK, name, last_update)
        city(city_id PK, city, country_id FK->country, last_update)
        country(country_id PK, country, last_update)
        customer(customer_id PK, store_id FK->store, first_name, last_name, email, address_id FK->address, activebool, create_date, last_update, active, uuid)
        film(film_id PK, title, description, release_year, language_id FK->language, original_language_id FK->language, rental_duration, rental_rate, length, replacement_cost, rating, last_update, special_features, fulltext, length_hours)
        film_actor(actor_id PK FK->actor, film_id PK FK->film, last_update)
        film_category(film_id PK FK->film, category_id PK FK->category, last_update)
        inventory(inventory_id PK, film_id FK->film, store_id FK->store, last_update)
        language(language_id PK, name, last_update)
        payment(payment_id PK, customer_id, staff_id, rental_id, amount, payment_date PK, uuid)
        rental(rental_id PK, rental_date, inventory_id FK->inventory, customer_id FK->customer, return_date, staff_id FK->staff, last_update, uuid)
        staff(staff_id PK, first_name, last_name, address_id FK->address, email, store_id FK->store, active, username, password, last_update, picture)
        store(store_id PK, manager_staff_id, address_id FK->address, last_update)

        Relationships Postgres does not declare as foreign keys, but which hold:
          store.manager_staff_id -> staff.staff_id
          payment.customer_id -> customer.customer_id, payment.staff_id -> staff.staff_id, payment.rental_id -> rental.rental_id
          (payment is partitioned, so its foreign keys are not declared on the parent table)
        """;

    public static IReadOnlyList<ToolDescriptor> All { get; } =
    [
        new()
        {
            Name = "get_schema",
            Kind = ToolKind.Schema,
            Description =
                "Return the columns, primary keys and foreign keys of every table in the database. " +
                "Takes no arguments. Call this before writing SQL if you are unsure of a table or column name.",
        },
        new()
        {
            Name = "execute_sql",
            Kind = ToolKind.FreeSql,
            Description =
                "Run a single read-only SELECT against the database and return the rows. " +
                "Joins, aggregates and subqueries are all allowed. One statement only: no INSERT, " +
                "UPDATE, DELETE or DDL, and no multiple statements. " +
                $"At most {MaxRows} rows are shown, but the true total is always stated.",
            Parameters =
            [
                ToolParameter.Term("query", "A single read-only SQL SELECT statement.")
                    with { MinLength = 8, MaxLength = 4000 },
            ],
        },
    ];

    public static IReadOnlyDictionary<string, ToolDescriptor> ByName { get; } =
        All.ToDictionary(t => t.Name, StringComparer.Ordinal);
}
