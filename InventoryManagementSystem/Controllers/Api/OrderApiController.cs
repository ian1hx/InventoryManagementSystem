﻿using InventoryManagementSystem.Models.EF;
using InventoryManagementSystem.Models.ResultModels;
using InventoryManagementSystem.Models.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers.Api
{

    [Route("[controller]/[action]")]
    [ApiController]
    public class OrderApiController : ControllerBase
    {

        private readonly InventoryManagementSystemContext _dbContext;

        public OrderApiController(InventoryManagementSystemContext dbContext)
        {
            _dbContext = dbContext;
        }


        /*
         * OrderApi/MakeOrder
         */
        // 下訂單
        [HttpPost]
        [Consumes("application/json")]
        public async Task<IActionResult> MakeOrder(MakeOrderViewModel model)
        {
            Order order = new Order
            {
                UserId = model.UserId,
                EquipmentId = model.EquipmentId,
                Quantity = model.Quantity,
                EstimatedPickupTime = model.EstimatedPickupTime,
                Day = model.Day,

                // 前端沒權限給的
                OrderStatusId = "P",
                OrderTime = DateTime.Now
            };

            _dbContext.Orders.Add(order);

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                return Conflict();
            }

            return Ok();

        }

        /*
         * OrderApi/GetOrdersByUserId/{UserID}
         */
        // 以 UserID 查詢所有訂單（任何狀態的訂單都會查出來）
        [HttpGet]
        [Produces("application/json")]
        [Route("{id}")]
        public async Task<OrderResultModel[]> GetOrdersByUserId(int id)
        {
            var results = await _dbContext.Orders
                .Where(o => o.UserId == id)
                .Select(o => new OrderResultModel
                {
                    OrderId = o.OrderId,
                    UserId = o.UserId,
                    EquipmentId = o.EquipmentId,
                    Quantity = o.Quantity,
                    EstimatedPickupTime = o.EstimatedPickupTime,
                    Day = o.Day,
                    OrderStatusId = o.OrderStatusId,
                    OrderTime = o.OrderTime,

                    EquipmentSn = o.Equipment.EquipmentSn,
                    EquipmentName = o.Equipment.EquipmentName,
                    Brand = o.Equipment.Brand,
                    Model = o.Equipment.Model,
                    UnitPrice = o.Equipment.UnitPrice,
                    Description = o.Equipment.Description,

                    Username = o.User.Username,

                    StatusName = o.OrderStatus.StatusName
                })
                .ToArrayAsync();

            return results;
        }

        [HttpPost]
        [Produces("application/json")]
        [Consumes("application/json")]
        public async Task<IActionResult> RespondOrder(RespondOrderViewModel model)
        {
            var order = await _dbContext.Orders
                .FirstOrDefaultAsync(o => o.OrderId == model.OrderID);

            // 防止同個 item 被分配多次
            int[] itemIDs = model.ItemIDs.Distinct().ToArray();

            Item[] items = await _dbContext.Items
                .Where(i => itemIDs.Contains(i.ItemId))
                .ToArrayAsync();

            // 找不到訂單
            if(order == null)
            {
                return BadRequest();
            }

            Response response = new Response
            {
                OrderId = model.OrderID,
                AdminId = model.AdminID,
            };

            if(model.Reply == "N")
            {
                order.OrderStatusId = "D"; // Denied
                response.Reply = "N"; // No
            }
            else if(model.Reply == "Y")
            {
                // 訂單寫的數量與實際分配的數量不一致
                if(order.Quantity != itemIDs.Length)
                {
                    return BadRequest();
                }

                // 存在有分配的設備非訂單所寫的設備
                bool invalidEquipIdExists = items
                    .Any(i => i.EquipmentId != order.EquipmentId);
                
                if(invalidEquipIdExists)
                {
                    return BadRequest();
                }

                // 庫存不夠，無法滿足訂單
                int inStockNumber = await _dbContext.Items
                    .AsNoTracking()
                    .Where(i => i.EquipmentId == order.EquipmentId)
                    .CountAsync(i => i.ConditionId == "I");
                if(inStockNumber < order.Quantity)
                {
                    return BadRequest();
                }


                response.Reply = "Y";
            }
            else
            {
                // REPLY 格式不正確
                return BadRequest("REPLY 格式不正確");
            }

            _dbContext.Responses.Add(response);
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                // 資料庫更新失敗
                return Conflict();
            }

            // 這裡 condition 也可以用 itemIDs.length
            // 因為執行到這邊已經保證 items 跟 itemIDs 長度一樣
            for(int i = 0; i < items.Length; i++)
            {
                items[i].ConditionId = "P"; // Pending
            }

            // 每個 item 都要新增一筆 OrderDetail 的記錄
            OrderDetail[] details = new OrderDetail[items.Length];
            for(int i = 0; i < items.Length; i++)
            {
                details[i] = new OrderDetail
                {
                    OrderId = model.OrderID,
                    ItemId = items[i].ItemId,
                    OrderDetailStatusId = "P" // Pending
                };

            }
            _dbContext.OrderDetails.AddRange(details);

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                return Conflict();
            }


            ItemLog[] logs = new ItemLog[items.Length];
            for(int i = 0; i < items.Length; i++)
            {
                logs[i] = new ItemLog
                {
                    OrderDetailId = details[i].OrderDetailId,
                    AdminId = model.AdminID,
                    ItemId = details[i].ItemId,
                    ConditionId = "P"  // Pending
                };
            }
            _dbContext.ItemLogs.AddRange(logs);

            order.OrderStatusId = "A"; // Approved

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                return Conflict();
            }

            return Ok();
        }

        /*
         * OrderApi/CancelOrder
         */
        // 取消 Order
        [HttpPost]
        [Consumes("application/json")]
        public async Task<IActionResult> CancelOrder(CancelOrderViewModel model)
        {
            Order order = await _dbContext.Orders
                .FindAsync(model.OrderID);


            if(order == null)
            {
                return NotFound();
            }


            OrderDetail[] details = await _dbContext.OrderDetails
                .Where(od => od.OrderId == order.OrderId)
                .ToArrayAsync();

            bool itemsTakenUnderTheOrder = details
                .Any(od => od.OrderDetailStatusId == "T");

            if(itemsTakenUnderTheOrder)
            {
                return BadRequest();
            }

            // 訂單改為取消狀態
            order.OrderStatusId = "C";

            // CanceledOrder 新增一筆紀錄
            CanceledOrder co = new CanceledOrder
            {
                OrderId = order.OrderId,
                UserId = order.UserId,
                Description = model.Description,
                CancelTime = DateTime.Now
            };
            _dbContext.CanceledOrders.Add(co);


            // 若 admin 已分配 item 給這筆 order
            // 還要再額外
            // 1. 把 item 的 condition 改回再庫（ItemLog 也要記錄 item 的改變）
            // 2. 把 order detail 的 status 改成取消
            if(details.Length != 0)
            {
                int[] itemIDs = details
                    .Select(od => od.ItemId)
                    .ToArray();

                Item[] items = await _dbContext.Items
                    .Where(i => itemIDs.Contains(i.ItemId))
                    .ToArrayAsync();

                foreach(Item item in items)
                {
                    // item 的狀態改成入庫
                    item.ConditionId = "I";
                }

                foreach(OrderDetail detail in details)
                {
                    // order detail 的狀態改成取消
                    detail.OrderDetailStatusId = "C";


                    // ItemLog 新增一筆資料
                    ItemLog log = new ItemLog
                    {
                        OrderDetailId = detail.OrderDetailId,
                        AdminId = model.AdminID,
                        ItemId = detail.ItemId,
                        ConditionId = "I",
                        Description = model.Description,
                        CreateTime = DateTime.Now
                    };

                    _dbContext.ItemLogs.Add(log);
                }
            }

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                return Conflict();
            }

            return Ok();
        }
    }
}
