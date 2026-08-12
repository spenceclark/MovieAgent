namespace MovieAgent.Tools;

/// <summary>
/// Every tool the harness knows about. Surfaces (see <see cref="ToolSurfaces"/>) are subsets
/// of this list.
/// </summary>
/// <remarks>
/// Two rules hold for every entry and are what make the hop-depth measurement meaningful:
/// <para>
/// <b>Search returns identifiers only.</b> <c>search_film</c> gives back film_id and title,
/// not the whole row. Reading the row is a separate hop.
/// </para>
/// <para>
/// <b>Foreign keys stay unresolved.</b> <c>get_film</c> returns <c>language_id = 1</c>.
/// Turning that into "English" costs the model another call to <c>get_language</c>.
/// </para>
/// Identifier ranges below come from the loaded database and are stated in descriptions so
/// the model can tell an out-of-range guess from a genuine miss. They are hints, not
/// guarantees â€” address, rental and payment identifiers have gaps.
/// </remarks>
public static class ToolCatalogue
{
    public const int MaxFilmId = 1000;
    public const int MaxActorId = 200;
    public const int MaxCustomerId = 999;
    public const int MaxCategoryId = 16;
    public const int MaxLanguageId = 6;
    public const int MaxInventoryId = 4581;
    public const int MaxRentalId = 87559;
    public const int MaxAddressId = 1005;
    public const int MaxCityId = 600;
    public const int MaxCountryId = 109;
    public const int MaxStoreId = 499;
    public const int MaxStaffId = 1499;

    public static IReadOnlyList<ToolDescriptor> All { get; } =
    [
        // ---------------------------------------------------------------- search
        new()
        {
            Name = "search_film",
            Description =
                "Find films whose title contains the given text (case-insensitive). " +
                "Returns film_id and title only. Use get_film to read a film's details.",
            Table = "film",
            Sql = "select film_id, title from film where title ilike '%' || @title_contains || '%' order by film_id",
            Parameters = [ToolParameter.Term("title_contains", "Text to look for anywhere in the film title.")],
            MaxRows = 25,
            EmptyResultHint = "No film title contains that text. Check the spelling or try a shorter fragment.",
        },
        new()
        {
            // Separate from search_film so description search is a surface variable rather than a
            // permanent widening of the title search. Keeping the two apart also means neither
            // tool has to explain which column a row matched on.
            Name = "search_film_description",
            Description =
                "Find films whose plot description contains the given text (case-insensitive). " +
                "Use this to find films by what happens in them, for example a character or a setting. " +
                "All the given words must appear, in any order; wording and word order do not need " +
                "to match the description exactly. Plot descriptions are formulaic and reuse a small " +
                "set of phrases, so few words match many films and every extra word narrows it. " +
                "Example: 'killer clown new york'. would find a film whose description was 'a new york cop who is on holiday in europe encounters a killer clown.'. " +
                "Returns film_id and title only.",
            Table = "film",
            // Full-text rather than a contiguous ILIKE substring. Measured: with ILIKE the model
            // paraphrased "Sumo Wrestler in Ancient Japan" as "sumo wrestler ancient Japan", dropped
            // the stopword, and got NO ROWS at hop 1 — so the run measured string luck rather than
            // planning. plainto_tsquery ANDs the stemmed terms and ignores stopwords and word order.
            // Still one table, still no relationship resolved.
            Sql =
                "select film_id, title from film " +
                "where to_tsvector('english', description) @@ plainto_tsquery('english', @description_contains) " +
                "order by film_id",
            Parameters = [ToolParameter.Term("description_contains", "Words that must all appear in the film's plot description.")],
            MaxRows = 25,
            EmptyResultHint = "No film description contains that text. Try a shorter or more common phrase.",
        },
        new()
        {
            Name = "search_actor",
            Description =
                "Find actors whose name contains the given text (case-insensitive). " +
                "A full name, a first name or a last name all work. " +
                "Returns actor_id, first_name and last_name.",
            Table = "actor",
            // Matched against the concatenated full name, not each column separately. Testing
            // the columns individually meant a full name could never match anything, because no
            // single column contains one: search_actor('PENELOPE GUINESS') returned NO ROWS for
            // an actor that exists, and the model retried it identically until the cap.
            Sql =
                "select actor_id, first_name, last_name from actor " +
                "where first_name || ' ' || last_name ilike '%' || @name_contains || '%' " +
                "order by actor_id",
            Parameters = [ToolParameter.Term("name_contains", "Text to look for in the actor's first or last name.")],
            MaxRows = 25,
            EmptyResultHint = "No actor name contains that text.",
        },
        new()
        {
            Name = "search_customer",
            Description =
                "Find customers whose name or email contains the given text (case-insensitive). " +
                "A full name, a first name, a last name or part of an email all work. " +
                "Returns customer_id, first_name and last_name.",
            Table = "customer",
            // Full name concatenated, for the same reason as search_actor.
            Sql =
                "select customer_id, first_name, last_name from customer " +
                "where first_name || ' ' || last_name ilike '%' || @text_contains || '%' " +
                "or email ilike '%' || @text_contains || '%' " +
                "order by customer_id",
            Parameters = [ToolParameter.Term("text_contains", "Text to look for in the customer's last name or email.")],
            MaxRows = 25,
            EmptyResultHint = "No customer last name or email contains that text.",
        },
        new()
        {
            Name = "search_category",
            Description = "Find film categories whose name contains the given text. Returns category_id and name.",
            Table = "category",
            Sql = "select category_id, name from category where name ilike '%' || @name_contains || '%' order by category_id",
            Parameters = [ToolParameter.Term("name_contains", "Text to look for in the category name.")],
            EmptyResultHint = "No category name contains that text.",
        },

        // ------------------------------------------------------------------ read
        new()
        {
            Name = "get_film",
            Description =
                "Read one film by its film_id. Returns language_id and original_language_id as numbers; " +
                "use get_language to turn a language_id into a language name.",
            Table = "film",
            Sql =
                "select film_id, title, description, release_year, language_id, original_language_id, " +
                "rental_duration, rental_rate, length, replacement_cost, rating " +
                "from film where film_id = @film_id",
            Parameters = [ToolParameter.Id("film_id", $"Film identifier, 1 to {MaxFilmId}.", MaxFilmId)],
            EmptyResultHint = $"There is no film with that film_id. Valid film_id values run 1 to {MaxFilmId}.",
        },
        new()
        {
            Name = "get_actor",
            Description = "Read one actor by actor_id. Returns first_name and last_name.",
            Table = "actor",
            Sql = "select actor_id, first_name, last_name from actor where actor_id = @actor_id",
            Parameters = [ToolParameter.Id("actor_id", $"Actor identifier, 1 to {MaxActorId}.", MaxActorId)],
            EmptyResultHint = $"There is no actor with that actor_id. Valid actor_id values run 1 to {MaxActorId}.",
        },
        new()
        {
            Name = "get_category",
            Description = "Read one category by category_id. Returns the category name.",
            Table = "category",
            Sql = "select category_id, name from category where category_id = @category_id",
            Parameters = [ToolParameter.Id("category_id", $"Category identifier, 1 to {MaxCategoryId}.", MaxCategoryId)],
            EmptyResultHint = $"There is no category with that category_id. Valid values run 1 to {MaxCategoryId}.",
        },
        new()
        {
            Name = "get_language",
            Description = "Read one language by language_id. Returns the language name.",
            Table = "language",
            Sql = "select language_id, name from language where language_id = @language_id",
            Parameters = [ToolParameter.Id("language_id", $"Language identifier, 1 to {MaxLanguageId}.", MaxLanguageId)],
            EmptyResultHint = $"There is no language with that language_id. Valid values run 1 to {MaxLanguageId}.",
        },
        new()
        {
            Name = "get_customer",
            Description =
                "Read one customer by customer_id. Returns address_id and store_id as numbers; " +
                "use get_address and get_store to resolve them.",
            Table = "customer",
            Sql =
                "select customer_id, first_name, last_name, email, address_id, store_id, activebool, create_date " +
                "from customer where customer_id = @customer_id",
            Parameters = [ToolParameter.Id("customer_id", $"Customer identifier, 1 to {MaxCustomerId}.", MaxCustomerId)],
            EmptyResultHint = $"There is no customer with that customer_id. Valid values run 1 to {MaxCustomerId}.",
        },
        new()
        {
            Name = "get_address",
            Description = "Read one address by address_id. Returns city_id as a number; use get_city to resolve it.",
            Table = "address",
            Sql =
                "select address_id, address, district, city_id, postal_code, phone " +
                "from address where address_id = @address_id",
            Parameters = [ToolParameter.Id("address_id", $"Address identifier, 1 to {MaxAddressId}.", MaxAddressId)],
            EmptyResultHint = $"There is no address with that address_id. Valid values run 1 to {MaxAddressId}, with gaps.",
        },
        new()
        {
            Name = "get_city",
            Description = "Read one city by city_id. Returns country_id as a number; use get_country to resolve it.",
            Table = "city",
            Sql = "select city_id, city, country_id from city where city_id = @city_id",
            Parameters = [ToolParameter.Id("city_id", $"City identifier, 1 to {MaxCityId}.", MaxCityId)],
            EmptyResultHint = $"There is no city with that city_id. Valid values run 1 to {MaxCityId}.",
        },
        new()
        {
            Name = "get_country",
            Description = "Read one country by country_id. Returns the country name.",
            Table = "country",
            Sql = "select country_id, country from country where country_id = @country_id",
            Parameters = [ToolParameter.Id("country_id", $"Country identifier, 1 to {MaxCountryId}.", MaxCountryId)],
            EmptyResultHint = $"There is no country with that country_id. Valid values run 1 to {MaxCountryId}.",
        },
        new()
        {
            Name = "get_store",
            Description =
                "Read one store by store_id. Returns manager_staff_id and address_id as numbers; " +
                "use get_staff and get_address to resolve them.",
            Table = "store",
            Sql = "select store_id, manager_staff_id, address_id from store where store_id = @store_id",
            Parameters = [ToolParameter.Id("store_id", $"Store identifier, 0 to {MaxStoreId}.", MaxStoreId) with { Minimum = 0 }],
            EmptyResultHint = $"There is no store with that store_id. Valid values run 0 to {MaxStoreId}.",
        },
        new()
        {
            Name = "get_staff",
            Description = "Read one staff member by staff_id. Returns address_id and store_id as numbers.",
            Table = "staff",
            Sql =
                "select staff_id, first_name, last_name, email, address_id, store_id, active, username " +
                "from staff where staff_id = @staff_id",
            Parameters = [ToolParameter.Id("staff_id", $"Staff identifier, 0 to {MaxStaffId}.", MaxStaffId) with { Minimum = 0 }],
            EmptyResultHint = $"There is no staff member with that staff_id. Valid values run 0 to {MaxStaffId}.",
        },
        new()
        {
            Name = "get_inventory_item",
            Description =
                "Read one inventory item by inventory_id. Returns film_id and store_id as numbers. " +
                "An inventory item is one physical copy of a film held at one store.",
            Table = "inventory",
            Sql = "select inventory_id, film_id, store_id from inventory where inventory_id = @inventory_id",
            Parameters = [ToolParameter.Id("inventory_id", $"Inventory identifier, 1 to {MaxInventoryId}.", MaxInventoryId)],
            EmptyResultHint = $"There is no inventory item with that inventory_id. Valid values run 1 to {MaxInventoryId}.",
        },
        new()
        {
            Name = "get_rental",
            Description =
                "Read one rental by rental_id. Returns inventory_id, customer_id and staff_id as numbers. " +
                "A rental refers to an inventory item, not directly to a film.",
            Table = "rental",
            Sql =
                "select rental_id, rental_date, inventory_id, customer_id, return_date, staff_id " +
                "from rental where rental_id = @rental_id",
            Parameters = [ToolParameter.Id("rental_id", $"Rental identifier, 1 to {MaxRentalId}.", MaxRentalId)],
            EmptyResultHint = $"There is no rental with that rental_id. Valid values run 1 to {MaxRentalId}, with gaps.",
        },

        // --------------------------------------------------------------- link up
        // Junction and foreign-key index tools. Each reads exactly one table and returns
        // identifiers for the model to resolve itself.
        new()
        {
            Name = "get_film_actor_ids",
            Description =
                "List the actor_id of every actor credited in a film. Returns identifiers only; " +
                "use get_actor to turn each actor_id into a name.",
            Table = "film_actor",
            Sql = "select actor_id from film_actor where film_id = @film_id order by actor_id",
            Parameters = [ToolParameter.Id("film_id", $"Film identifier, 1 to {MaxFilmId}.", MaxFilmId)],
            MaxRows = 50,
            EmptyResultHint = "That film has no credited actors, or the film_id does not exist.",
        },
        new()
        {
            Name = "get_actor_film_ids",
            Description =
                "List the film_id of every film an actor is credited in. Returns identifiers only; " +
                "use get_film to turn each film_id into a title.",
            Table = "film_actor",
            Sql = "select film_id from film_actor where actor_id = @actor_id order by film_id",
            Parameters = [ToolParameter.Id("actor_id", $"Actor identifier, 1 to {MaxActorId}.", MaxActorId)],
            MaxRows = 50,
            EmptyResultHint = "That actor has no credited films, or the actor_id does not exist.",
        },
        new()
        {
            Name = "get_film_category_ids",
            Description =
                "List the category_id of every category a film belongs to. A film may belong to several. " +
                "Use get_category to turn each category_id into a name.",
            Table = "film_category",
            Sql = "select category_id from film_category where film_id = @film_id order by category_id",
            Parameters = [ToolParameter.Id("film_id", $"Film identifier, 1 to {MaxFilmId}.", MaxFilmId)],
            EmptyResultHint = "That film has no categories, or the film_id does not exist.",
        },
        new()
        {
            Name = "get_category_film_ids",
            Description = "List the film_id of every film in a category. Returns identifiers only.",
            Table = "film_category",
            Sql = "select film_id from film_category where category_id = @category_id order by film_id",
            Parameters = [ToolParameter.Id("category_id", $"Category identifier, 1 to {MaxCategoryId}.", MaxCategoryId)],
            MaxRows = 50,
            EmptyResultHint = "That category has no films, or the category_id does not exist.",
        },
        new()
        {
            Name = "get_film_inventory_ids",
            Description =
                "List the inventory items holding copies of a film. Returns inventory_id and store_id. " +
                "Use get_store to resolve a store_id.",
            Table = "inventory",
            Sql = "select inventory_id, store_id from inventory where film_id = @film_id order by inventory_id",
            Parameters = [ToolParameter.Id("film_id", $"Film identifier, 1 to {MaxFilmId}.", MaxFilmId)],
            MaxRows = 30,
            EmptyResultHint = "No store holds a copy of that film, or the film_id does not exist.",
        },
        new()
        {
            Name = "get_customer_rental_ids",
            Description =
                "List a customer's rentals. Returns rental_id, inventory_id, rental_date and return_date. " +
                "An inventory_id must be resolved via get_inventory_item to reach a film.",
            Table = "rental",
            Sql =
                "select rental_id, inventory_id, rental_date, return_date from rental " +
                "where customer_id = @customer_id order by rental_id",
            Parameters = [ToolParameter.Id("customer_id", $"Customer identifier, 1 to {MaxCustomerId}.", MaxCustomerId)],
            MaxRows = 30,
            EmptyResultHint = "That customer has no rentals, or the customer_id does not exist.",
        },
        new()
        {
            Name = "get_inventory_rental_ids",
            Description =
                "List the rentals of one inventory item. Returns rental_id, customer_id, rental_date " +
                "and return_date. Use get_customer to turn a customer_id into a name.",
            Table = "rental",
            // Dates are projected here for the same reason get_customer_rental_ids projects them:
            // they are columns of the same row, so returning them resolves no relationship. Without
            // them, filtering a film's rentals by year costs one get_rental call per rental, which
            // exhausts the iteration budget on bookkeeping rather than on planning.
            Sql =
                "select rental_id, customer_id, rental_date, return_date from rental " +
                "where inventory_id = @inventory_id order by rental_id",
            Parameters = [ToolParameter.Id("inventory_id", $"Inventory identifier, 1 to {MaxInventoryId}.", MaxInventoryId)],
            MaxRows = 30,
            EmptyResultHint = "That inventory item has never been rented, or the inventory_id does not exist.",
        },
        new()
        {
            Name = "get_customer_payments",
            Description = "List a customer's payments. Returns payment_id, rental_id, amount and payment_date.",
            Table = "payment",
            Sql =
                "select payment_id, rental_id, amount, payment_date from payment " +
                "where customer_id = @customer_id order by payment_id",
            Parameters = [ToolParameter.Id("customer_id", $"Customer identifier, 1 to {MaxCustomerId}.", MaxCustomerId)],
            MaxRows = 30,
            EmptyResultHint = "That customer has no payments, or the customer_id does not exist.",
        },

        // ------------------------------------------------------- counting variant
        // Only in the enriched surface. Deliberately a fixed, narrow set rather than a
        // generic count(table) tool â€” a generic one drifts back towards execute_sql.
        new()
        {
            Name = "count_films",
            Description = "Count all films in the catalogue.",
            Table = "film",
            Sql = "select count(*) as film_count from film",
        },
        new()
        {
            Name = "count_film_actors",
            Description = "Count the actors credited in one film.",
            Table = "film_actor",
            Sql = "select count(*) as actor_count from film_actor where film_id = @film_id",
            Parameters = [ToolParameter.Id("film_id", $"Film identifier, 1 to {MaxFilmId}.", MaxFilmId)],
        },
        new()
        {
            Name = "count_actor_films",
            Description = "Count the films one actor is credited in.",
            Table = "film_actor",
            Sql = "select count(*) as film_count from film_actor where actor_id = @actor_id",
            Parameters = [ToolParameter.Id("actor_id", $"Actor identifier, 1 to {MaxActorId}.", MaxActorId)],
        },
        new()
        {
            Name = "count_category_films",
            Description = "Count the films in one category.",
            Table = "film_category",
            Sql = "select count(*) as film_count from film_category where category_id = @category_id",
            Parameters = [ToolParameter.Id("category_id", $"Category identifier, 1 to {MaxCategoryId}.", MaxCategoryId)],
        },
        new()
        {
            Name = "count_customer_rentals",
            Description = "Count one customer's rentals.",
            Table = "rental",
            Sql = "select count(*) as rental_count from rental where customer_id = @customer_id",
            Parameters = [ToolParameter.Id("customer_id", $"Customer identifier, 1 to {MaxCustomerId}.", MaxCustomerId)],
        },
    ];

    public static IReadOnlyDictionary<string, ToolDescriptor> ByName { get; } =
        All.ToDictionary(t => t.Name, StringComparer.Ordinal);
}
