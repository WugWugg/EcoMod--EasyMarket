Niche mod made for admins at Copper Village.

 1. Install DLL to Mods/UserCode.
 2. Setup config `/Configs/EasyMarket.eco`:
  ```
  {
    "CurrencyName": "Coins",
    "OwnerName": "The Market"
  }
  ```
 3. Boot the server up.
 4. Create the user, currency, and spawn in money: `/easymarket init 1000000`
 5. Now you can quickly setup depos with: `/easymarket create`
 6. To see the store templates available: `/easymarket list`
 7. Look at a store and run `/easymarket save TemplateName` to save the current offers to file.
 8. Look at another store and run `/easymarket load TemplateName` to import those offers into that store.
 9. If you need to add money to the EasyMarket player: `/easymarket fund 100`
 10. To remove money: `/easymarket defund 100`
 
